using System;
using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 通用非法内存读写与野指针访问规则 (General Access Violation / Wild Pointer)
    /// 具备证据链严谨推导、置信度动态评估与根因角色区分能力
    /// </summary>
    public sealed class AccessViolationRule : IDiagnosticRule
    {
        public string RuleName => "AccessViolation";

        public string RuleId => "RULE-AV-001";

        public string Version => "2.0.0";

        public string Description => "检测非法地址访问、野指针解引用、缓冲区越界或已释放内存(Use-After-Free)，拆分异常爆发点与直接触发点。";

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

            // 如果不是空指针页（大于 0x10000），则属于野指针或越界非法访问
            if (!exp.TargetMemoryAddress.HasValue || exp.TargetMemoryAddress.Value >= 0x10000)
            {
                var crashThread = dumpReport.Threads.FirstOrDefault(t => t.ThreadId == exp.ThreadId)
                               ?? dumpReport.Threads.FirstOrDefault();

                var exceptionFrame = crashThread?.Frames.FirstOrDefault();
                var directTriggerFrame = crashThread?.Frames.FirstOrDefault(f => f.IsUserCode);
                var rootCauseCandidateFrame = crashThread?.Frames.FirstOrDefault(f => f.IsRootCause) ?? directTriggerFrame;

                ulong targetAddr = exp.TargetMemoryAddress ?? 0;
                string patternAnalysis = AnalyzeAddressPattern(targetAddr);
                bool hasSpecialPattern = patternAnalysis.StartsWith("特征命中");

                // 动态多维置信度评估
                double confidence = 0.60;
                if (exp.Parameters.Count >= 2)
                {
                    confidence += 0.10; // 具有完整的读写标志与非法目标地址参数
                }
                if (hasSpecialPattern)
                {
                    confidence += 0.15; // 命中特异性内存标记 (如已释放/未初始化堆)
                }
                if (directTriggerFrame != null && !string.IsNullOrEmpty(directTriggerFrame.SourceFile) && directTriggerFrame.LineNumber > 0)
                {
                    confidence += 0.10; // 具备精确源码行号支撑
                }
                if (dumpReport.Integrity.Status == DumpIntegrityStatus.Complete)
                {
                    confidence += 0.05; // Dump 完整无截断
                }
                confidence = Math.Min(0.98, Math.Max(0.50, confidence));

                // 构建故障归因链条
                var chain = new CodeLocationChain
                {
                    ExceptionPoint = exceptionFrame,
                    DirectTriggerPoint = directTriggerFrame,
                    RootCauseCandidatePoint = rootCauseCandidateFrame,
                    Certainty = hasSpecialPattern ? RootCauseCertainty.ProbableHypothesis : RootCauseCertainty.InsufficientEvidence
                };

                chain.UnconfirmedAssumptions.Add($"目标地址 0x{targetAddr:X16} 的失效原因推测为 {(hasSpecialPattern ? "堆已被提前释放或未初始化" : "野指针偏移计算错误或悬挂指针")}");
                chain.VerificationSteps.Add("开启 PageHeap 调试验证：在命令行执行 gflags.exe -i <进程名>.exe +hpa，使堆越界与悬挂访问在发生的瞬间崩溃并记录第一现场栈。");
                chain.VerificationSteps.Add("检查指针生命周期：复核该指针所指向缓冲区的分配与销毁配对关系，确认是否存在多线程并发操作或跨函数提前释放。");

                var primaryFrame = chain.PrimaryNavigationPoint;

                var finding = new DiagnosticFinding
                {
                    RuleId = RuleId,
                    RuleVersion = Version,
                    Level = DiagnosticLevel.Critical,
                    Title = $"🔴 内存访问违例与野指针 (Access Violation - {patternAnalysis})",
                    Description = $"目标程序发生非法内存操作（{exp.AccessType}），目标地址 0x{targetAddr:X16} 不属于进程有效的提交虚拟内存页。\n" +
                                  $"【归因说明】异常爆发于指令 0x{exp.ExceptionAddress:X16}，业务直接触发入口为 {directTriggerFrame?.ModuleName ?? "Unknown"}!{directTriggerFrame?.FunctionName ?? "Unknown"}。\n" +
                                  $"【特征分析】{patternAnalysis}",
                    Module = primaryFrame?.ModuleName ?? "Unknown",
                    Function = primaryFrame?.FunctionName ?? "Unknown",
                    SourceFile = primaryFrame?.SourceFile ?? string.Empty,
                    Line = primaryFrame?.LineNumber ?? 0,
                    Confidence = confidence,
                    LocationRole = directTriggerFrame != null ? CodeLocationRole.DirectTriggerCall : CodeLocationRole.ExceptionInstruction,
                    LocationChain = chain
                };

                // 结构化证据链条
                finding.AddStructuredEvidence(
                    EvidenceCategory.ConfirmedFact,
                    $"异常代码: 0xC0000005 (STATUS_ACCESS_VIOLATION)，访问类型: {exp.AccessType}",
                    1.0,
                    exp.ExceptionAddress);

                finding.AddStructuredEvidence(
                    EvidenceCategory.ConfirmedFact,
                    $"硬件指令指针: 0x{exp.ExceptionAddress:X16}，目标非法地址: 0x{targetAddr:X16}",
                    1.0,
                    targetAddr);

                finding.AddStructuredEvidence(
                    EvidenceCategory.SupportingEvidence,
                    $"特征识别: {patternAnalysis}",
                    hasSpecialPattern ? 0.9 : 0.6);

                if (exceptionFrame != null)
                {
                    finding.AddStructuredEvidence(
                        EvidenceCategory.SupportingEvidence,
                        $"异常发生底层栈帧: {exceptionFrame.ModuleName}!{exceptionFrame.FunctionName}",
                        0.8,
                        exceptionFrame.InstructionOffset,
                        exceptionFrame.ModuleName);
                }

                if (directTriggerFrame != null && directTriggerFrame != exceptionFrame)
                {
                    finding.AddStructuredEvidence(
                        EvidenceCategory.SupportingEvidence,
                        $"业务直接触发调用: {directTriggerFrame.ModuleName}!{directTriggerFrame.FunctionName} [{directTriggerFrame.SourceFile}:{directTriggerFrame.LineNumber}]",
                        0.9,
                        directTriggerFrame.InstructionOffset,
                        directTriggerFrame.ModuleName);
                }

                finding.AddStructuredEvidence(
                    EvidenceCategory.Hypothesis,
                    $"诊断假设: 目标内存 0x{targetAddr:X16} 损坏或悬挂，推测破坏发生在指针解引用前序逻辑",
                    0.7);

                finding.AddStructuredEvidence(
                    EvidenceCategory.VerificationStep,
                    "开启 PageHeap (gflags / +hpa) 并重新抓取 Dump 复核",
                    1.0);

                finding.Recommendations.Add("排查野指针或悬挂指针 (Dangling Pointer)，确认指针所指向的堆内存是否已被提前 free/delete。");
                finding.Recommendations.Add("排查数组、缓冲区越界拷贝（如 memcpy/strcpy/sprintf），确认长度计算是否溢出导致覆盖了指针。");
                finding.Recommendations.Add("建议在调试配置下开启 Pageheap (gflags.exe -i your_app.exe +hpa) 以便在越界或悬挂访问的瞬间立即触发精准定位。");

                if (crashThread?.Frames != null)
                {
                    finding.RelatedStack.AddRange(crashThread.Frames.Take(10));
                }

                yield return finding;
            }
        }

        private static string AnalyzeAddressPattern(ulong address)
        {
            if (address == 0xFEEEFEEEFEEEFEEE || (address & 0xFFFFFFFF) == 0xFEEEFEEE)
            {
                return "特征命中 0xFEEEFEEE（堆已释放标记，疑似 Use-After-Free）";
            }
            if (address == 0xCDCDCDCDCDCDCDCD || (address & 0xFFFFFFFF) == 0xCDCDCDCD)
            {
                return "特征命中 0xCDCDCDCD（CRT 调试堆未初始化内存）";
            }
            if (address == 0xBAADF00DBAADF00D || (address & 0xFFFFFFFF) == 0xBAADF00D)
            {
                return "特征命中 0xBAADF00D（LocalAlloc 分配但未初始化内存）";
            }
            if (address == 0xCCCCCCCCCCCCCCCC || (address & 0xFFFFFFFF) == 0xCCCCCCCC)
            {
                return "特征命中 0xCCCCCCCC（栈上未初始化局部变量）";
            }

            return "常规未映射地址非法访问";
        }
    }
}
