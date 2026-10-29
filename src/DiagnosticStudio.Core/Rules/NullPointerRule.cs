using System;
using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 空指针解引用分析规则 (Null Pointer Dereference)
    /// 具备偏移结构体分析、证据链分层与置信度动态校准能力
    /// </summary>
    public sealed class NullPointerRule : IDiagnosticRule
    {
        public string RuleName => "NullPointerDereference";

        public string RuleId => "RULE-NULL-001";

        public string Version => "2.0.0";

        public string Description => "检测解引用空指针或空对象结构体成员引发的内存访问违例，推导成员偏移与责任链条。";

        public IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport)
        {
            if (dumpReport?.Exception == null)
            {
                yield break;
            }

            var exp = dumpReport.Exception;
            if (exp.ExceptionCode != 0xC0000005)
            {
                yield break;
            }

            // 检查目标内存地址是否在 NULL 保护页范围 (通常小于 64KB = 0x10000)
            if (exp.TargetMemoryAddress.HasValue && exp.TargetMemoryAddress.Value < 0x10000)
            {
                var crashThread = dumpReport.Threads.FirstOrDefault(t => t.ThreadId == exp.ThreadId)
                               ?? dumpReport.Threads.FirstOrDefault();

                var exceptionFrame = crashThread?.Frames.FirstOrDefault();
                var triggerUserFrame = crashThread?.Frames.FirstOrDefault(f => f.IsUserCode);
                var rootCauseCandidateFrame = crashThread?.Frames.FirstOrDefault(f => f.IsRootCause) ?? triggerUserFrame;

                ulong targetAddr = exp.TargetMemoryAddress.Value;
                bool isDirectZero = targetAddr == 0;
                string offsetDesc = isDirectZero
                    ? "直接空指针解引用 (*nullptr)"
                    : $"空对象成员偏移解引用 (this/ptr 为 nullptr, 访问成员偏移 +0x{targetAddr:X})";

                // 动态置信度评估
                double confidence = 0.85;
                if (triggerUserFrame != null && !string.IsNullOrEmpty(triggerUserFrame.SourceFile) && triggerUserFrame.LineNumber > 0)
                {
                    confidence += 0.10;
                }
                if (dumpReport.Integrity.Status == DumpIntegrityStatus.Complete)
                {
                    confidence += 0.03;
                }
                confidence = Math.Min(0.98, confidence);

                var chain = new CodeLocationChain
                {
                    ExceptionPoint = exceptionFrame,
                    DirectTriggerPoint = triggerUserFrame,
                    RootCauseCandidatePoint = rootCauseCandidateFrame,
                    Certainty = RootCauseCertainty.ConfirmedRootCause
                };

                chain.UnconfirmedAssumptions.Add(isDirectZero
                    ? "解引用的指针变量值为 NULL/nullptr"
                    : $"包含该成员的宿主对象指针值为 NULL/nullptr，当前访问位于成员偏移 0x{targetAddr:X}");
                chain.VerificationSteps.Add("检查当前函数入参、返回值或局部对象，在调用点前添加 Assert 或非空防护 (if (ptr != nullptr))。");
                chain.VerificationSteps.Add("复核在多线程并发场景下，该指针是否存在跨线程清空与并发调用的数据竞争。");

                var primaryFrame = chain.PrimaryNavigationPoint;

                var finding = new DiagnosticFinding
                {
                    RuleId = RuleId,
                    RuleVersion = Version,
                    Level = DiagnosticLevel.Critical,
                    Title = $"🔴 致命空指针解引用 ({offsetDesc})",
                    Description = $"目标程序在试图执行 {exp.AccessType} 操作时崩溃，访问的非法目标内存地址为 0x{targetAddr:X16}。\n" +
                                  $"该地址位于 Windows 保留空指针保护页 (0x0 ~ 0x10000) 内。\n" +
                                  $"【特征分析】{offsetDesc}。",
                    Module = primaryFrame?.ModuleName ?? "Unknown",
                    Function = primaryFrame?.FunctionName ?? "Unknown",
                    SourceFile = primaryFrame?.SourceFile ?? string.Empty,
                    Line = primaryFrame?.LineNumber ?? 0,
                    Confidence = confidence,
                    LocationRole = triggerUserFrame != null ? CodeLocationRole.DirectTriggerCall : CodeLocationRole.ExceptionInstruction,
                    LocationChain = chain
                };

                finding.AddStructuredEvidence(
                    EvidenceCategory.ConfirmedFact,
                    $"异常代码: 0x{exp.ExceptionCode:X8} ({exp.ExceptionName})，非法访问地址 0x{targetAddr:X16} 落在 NULL 保护页",
                    1.0,
                    targetAddr);

                finding.AddStructuredEvidence(
                    EvidenceCategory.ConfirmedFact,
                    $"崩溃硬件指令: 0x{exp.ExceptionAddress:X16}，操作类型: {exp.AccessType}",
                    1.0,
                    exp.ExceptionAddress);

                finding.AddStructuredEvidence(
                    EvidenceCategory.SupportingEvidence,
                    $"寻址特征: {offsetDesc}",
                    0.9);

                if (exceptionFrame != null)
                {
                    finding.AddStructuredEvidence(
                        EvidenceCategory.SupportingEvidence,
                        $"异常发生底层栈帧: {exceptionFrame.ModuleName}!{exceptionFrame.FunctionName}",
                        0.8,
                        exceptionFrame.InstructionOffset,
                        exceptionFrame.ModuleName);
                }

                if (triggerUserFrame != null && triggerUserFrame != exceptionFrame)
                {
                    finding.AddStructuredEvidence(
                        EvidenceCategory.SupportingEvidence,
                        $"直接触发业务代码: {triggerUserFrame.ModuleName}!{triggerUserFrame.FunctionName} [{triggerUserFrame.SourceFile}:{triggerUserFrame.LineNumber}]",
                        0.95,
                        triggerUserFrame.InstructionOffset,
                        triggerUserFrame.ModuleName);
                }

                finding.AddStructuredEvidence(
                    EvidenceCategory.Hypothesis,
                    "指针来源假设：未正确初始化、前序资源加载返回失败或已被其他逻辑提前析构置空",
                    0.8);

                finding.AddStructuredEvidence(
                    EvidenceCategory.VerificationStep,
                    "在源码中排查当前对象指针的判空保护与多线程同步",
                    1.0);

                finding.Recommendations.Add("检查崩溃函数中参与指针运算的入参或类成员变量，确认是否为 nullptr 或 NULL。");
                finding.Recommendations.Add("检查前序步骤是否有工厂创建、查找节点或资源加载失败未做非空判断 (if (ptr == nullptr)) 即直接调用的情况。");
                finding.Recommendations.Add("如果在多线程环境下，排查是否存在对象已被其他线程释放并置空、但当前线程未加锁保护导致的竞态条件。");

                if (crashThread?.Frames != null)
                {
                    finding.RelatedStack.AddRange(crashThread.Frames.Take(10));
                }

                yield return finding;
            }
        }
    }
}
