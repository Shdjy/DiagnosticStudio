using System;
using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 堆内存损坏与管理结构损毁分析规则 (Heap Corruption - 0xC0000374)
    /// 区分“堆损坏被动发现现场”与“破坏行为原始现场”，严格评估置信度
    /// </summary>
    public sealed class HeapCorruptionRule : IDiagnosticRule
    {
        public string RuleName => "HeapCorruption";

        public string RuleId => "RULE-HEAP-001";

        public string Version => "2.0.0";

        public string Description => "检测堆头踩踏、重复释放或堆元数据损坏，明确区分破坏发现点与原始破坏点。";

        public IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport)
        {
            if (dumpReport?.Exception == null)
            {
                yield break;
            }

            var exp = dumpReport.Exception;
            // 0xC0000374 = STATUS_HEAP_CORRUPTION
            if (exp.ExceptionCode != 0xC0000374)
            {
                yield break;
            }

            var crashThread = dumpReport.Threads.FirstOrDefault(t => t.ThreadId == exp.ThreadId)
                           ?? dumpReport.Threads.FirstOrDefault();

            var exceptionFrame = crashThread?.Frames.FirstOrDefault();
            var triggerUserFrame = crashThread?.Frames.FirstOrDefault(f => f.IsUserCode);
            var rootCauseCandidateFrame = crashThread?.Frames.FirstOrDefault(f => f.IsRootCause) ?? triggerUserFrame;

            // 动态置信度：堆损坏由于延迟爆发性，现场调用栈常为“受害者”，而非“破坏元凶”
            // 故现场定位置信度适度保守，避免误导开发者
            double confidence = 0.72;
            if (triggerUserFrame != null && !string.IsNullOrEmpty(triggerUserFrame.SourceFile) && triggerUserFrame.LineNumber > 0)
            {
                confidence += 0.08;
            }

            var chain = new CodeLocationChain
            {
                ExceptionPoint = exceptionFrame,
                DirectTriggerPoint = triggerUserFrame,
                RootCauseCandidatePoint = rootCauseCandidateFrame,
                Certainty = RootCauseCertainty.ProbableHypothesis
            };

            chain.UnconfirmedAssumptions.Add("当前栈帧上的分配或释放调用通常仅为发现元数据破坏的被动受害者，真正破坏堆头的写越界或双重释放可能发生在此前更早的业务代码中。");
            chain.VerificationSteps.Add("开启完整页堆调试：在目标环境执行 gflags.exe -p /enable <目标程序>.exe /full，使得任何写越界在触碰保护页的微秒级瞬间立即崩溃并捕获真凶。");
            chain.VerificationSteps.Add("检查当前直接触发点传入的指针地址是否合法，排查是否对未分配指针或野指针调用了 free/delete。");

            var primaryFrame = chain.PrimaryNavigationPoint;

            var finding = new DiagnosticFinding
            {
                RuleId = RuleId,
                RuleVersion = Version,
                Level = DiagnosticLevel.Critical,
                Title = "🔴 堆内存元数据损坏 (Heap Corruption - 0xC0000374)",
                Description = "Windows 底层堆管理器 (NT Heap) 在执行堆分配或释放操作时，校验发现堆块头部元数据已被篡改破坏。\n" +
                              "【重要提示】当前栈帧是操作系统拦截并终止进程的“发现位置”，真正发生堆破坏（越界写/双重释放）的代码可能在更早时刻执行完毕。",
                Module = primaryFrame?.ModuleName ?? "ntdll.dll",
                Function = primaryFrame?.FunctionName ?? "RtlReportCriticalFailure",
                SourceFile = primaryFrame?.SourceFile ?? string.Empty,
                Line = primaryFrame?.LineNumber ?? 0,
                Confidence = confidence,
                LocationRole = CodeLocationRole.DirectTriggerCall,
                LocationChain = chain
            };

            finding.AddStructuredEvidence(
                EvidenceCategory.ConfirmedFact,
                "异常代码: 0xC0000374 (STATUS_HEAP_CORRUPTION)，NT 堆管理器拦截到元数据损坏",
                1.0,
                exp.ExceptionAddress);

            finding.AddStructuredEvidence(
                EvidenceCategory.ConfirmedFact,
                $"崩溃拦截指令: 0x{exp.ExceptionAddress:X16}，受影响线程: TID {exp.ThreadId}",
                1.0,
                exp.ExceptionAddress,
                threadId: exp.ThreadId);

            if (exceptionFrame != null)
            {
                finding.AddStructuredEvidence(
                    EvidenceCategory.SupportingEvidence,
                    $"系统堆校验拦截点: {exceptionFrame.ModuleName}!{exceptionFrame.FunctionName}",
                    0.8,
                    exceptionFrame.InstructionOffset,
                    exceptionFrame.ModuleName);
            }

            if (triggerUserFrame != null && triggerUserFrame != exceptionFrame)
            {
                finding.AddStructuredEvidence(
                    EvidenceCategory.SupportingEvidence,
                    $"直接触发堆操作的业务代码: {triggerUserFrame.ModuleName}!{triggerUserFrame.FunctionName} [{triggerUserFrame.SourceFile}:{triggerUserFrame.LineNumber}]",
                    0.85,
                    triggerUserFrame.InstructionOffset,
                    triggerUserFrame.ModuleName);
            }

            finding.AddStructuredEvidence(
                EvidenceCategory.Hypothesis,
                "破坏假设: 可能存在踩踏相邻堆块头部 (Buffer Overflow into Heap Header) 或重复释放 (Double Free)",
                0.75);

            finding.AddStructuredEvidence(
                EvidenceCategory.VerificationStep,
                "开启 PageHeap (gflags /full) 抓取第一案发现场",
                1.0);

            finding.Recommendations.Add("排查是否存在对同一指针执行了两次 delete 或 free 的重复释放 (Double Free) 问题。");
            finding.Recommendations.Add("排查堆内存越界写入踩踏后方相邻堆块头部的行为（如写入超过了 malloc 分配的字节数）。");
            finding.Recommendations.Add("强烈建议开启页堆调试保护：运行命令行 gflags.exe -p /enable your_app.exe /full，精准在越界发生的指令瞬间捕获崩溃。");

            if (crashThread?.Frames != null)
            {
                finding.RelatedStack.AddRange(crashThread.Frames.Take(12));
            }

            yield return finding;
        }
    }
}
