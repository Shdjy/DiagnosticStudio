using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 通用未捕获崩溃异常全能兜底诊断规则 (General Crash Fallback Rule)
    /// </summary>
    public sealed class GeneralCrashFallbackRule : IDiagnosticRule
    {
        public string RuleName => "GeneralCrashFallback";

        public string Description => "全能托底诊断：当目标转储发生未被特定专有规则命中的异常时，自动提取崩溃现场、异常代码、责任线程与模块，杜绝诊断遗漏。";

        // 已有专门规则处理的异常代码集合
        private static readonly HashSet<uint> HandledExceptionCodes = new HashSet<uint>
        {
            0xC0000005, // STATUS_ACCESS_VIOLATION (由 NullPointerRule / AccessViolationRule 处理)
            0xC00000FD, // STATUS_STACK_OVERFLOW (由 StackOverflowRule 处理)
            0xC0000409, // STATUS_STACK_BUFFER_OVERRUN (由 BufferOverflowRule 处理)
            0xE06D7363, // STATUS_CPP_EH_EXCEPTION (由 UnhandledCppExceptionRule 处理)
            0xC0000094, // STATUS_INTEGER_DIVIDE_BY_ZERO (由 DivideByZeroRule 处理)
            0xC000008F, // STATUS_FLOAT_DIVIDE_BY_ZERO (由 DivideByZeroRule 处理)
            0xC0000374, // STATUS_HEAP_CORRUPTION (由 HeapCorruptionRule 处理)
            0x80000003, // STATUS_BREAKPOINT (由 AssertOrBreakpointRule 处理)
            0x80000004  // STATUS_SINGLE_STEP (由 AssertOrBreakpointRule 处理)
        };

        public IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport)
        {
            if (dumpReport?.Exception == null)
            {
                yield break;
            }

            var exp = dumpReport.Exception;
            if (HandledExceptionCodes.Contains(exp.ExceptionCode))
            {
                yield break;
            }

            var crashThread = dumpReport.Threads.FirstOrDefault(t => t.ThreadId == exp.ThreadId)
                           ?? dumpReport.Threads.FirstOrDefault();

            var rootCauseFrame = crashThread?.Frames.FirstOrDefault(f => f.IsRootCause)
                              ?? crashThread?.Frames.FirstOrDefault(f => f.IsUserCode)
                              ?? crashThread?.Frames.FirstOrDefault();

            var triggerFrame = crashThread?.Frames.FirstOrDefault();

            var finding = new DiagnosticFinding
            {
                Level = DiagnosticLevel.Critical,
                Title = $"🔴 致命未处理系统异常 ({exp.ExceptionName} - 0x{exp.ExceptionCode:X8})",
                Description = $"目标程序遭遇致命底层系统异常 (0x{exp.ExceptionCode:X8} - {exp.ExceptionName})，该异常未被任何上层应用逻辑捕获，导致进程被操作系统强行异常终止。",
                Module = rootCauseFrame?.ModuleName ?? triggerFrame?.ModuleName ?? "Unknown",
                Function = rootCauseFrame?.FunctionName ?? triggerFrame?.FunctionName ?? "Unknown",
                SourceFile = rootCauseFrame?.SourceFile ?? triggerFrame?.SourceFile ?? string.Empty,
                Line = rootCauseFrame?.LineNumber ?? triggerFrame?.LineNumber ?? 0,
                Confidence = 0.90
            };

            finding.Evidence.Add($"异常代码: 0x{exp.ExceptionCode:X8} ({exp.ExceptionName})");
            finding.Evidence.Add($"崩溃指令指针: 0x{exp.ExceptionAddress:X16}");
            finding.Evidence.Add($"崩溃故障线程: TID {exp.ThreadId}");

            if (triggerFrame != null)
            {
                finding.Evidence.Add($"系统顶层栈帧: {triggerFrame.ModuleName}!{triggerFrame.FunctionName}");
            }

            if (rootCauseFrame != null && rootCauseFrame != triggerFrame)
            {
                finding.Evidence.Add($"业务责任代码: {rootCauseFrame.ModuleName}!{rootCauseFrame.FunctionName}");
                if (!string.IsNullOrEmpty(rootCauseFrame.SourceFile))
                {
                    finding.Evidence.Add($"源码位置: {rootCauseFrame.SourceFile} 第 {rootCauseFrame.LineNumber} 行");
                }
            }

            if (exp.Parameters != null && exp.Parameters.Count > 0)
            {
                finding.Evidence.Add($"异常参数列表: {string.Join(", ", exp.Parameters.Select(p => $"0x{p:X}"))}");
            }

            finding.Recommendations.Add("检查崩溃函数附近的入参校验、资源句柄有效性及状态机流转。");
            finding.Recommendations.Add("参考 MSDN NTSTATUS 异常代码表，确认对应 0x" + exp.ExceptionCode.ToString("X8") + " 的系统触发原因。");

            if (crashThread?.Frames != null)
            {
                finding.RelatedStack.AddRange(crashThread.Frames.Take(10));
            }

            yield return finding;
        }
    }
}
