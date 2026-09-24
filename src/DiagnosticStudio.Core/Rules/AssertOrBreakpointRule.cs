using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 断言触发与断点异常分析规则 (Assert Failure & Breakpoint - 0x80000003)
    /// </summary>
    public sealed class AssertOrBreakpointRule : IDiagnosticRule
    {
        public string RuleName => "AssertOrBreakpoint";

        public string Description => "检测由 assert() 断言失败、__debugbreak() 主动硬编码断点或调试中断指令引发的异常。";

        public IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport)
        {
            if (dumpReport?.Exception == null)
            {
                yield break;
            }

            var exp = dumpReport.Exception;
            // 0x80000003 = STATUS_BREAKPOINT, 0x80000004 = STATUS_SINGLE_STEP
            if (exp.ExceptionCode != 0x80000003 && exp.ExceptionCode != 0x80000004)
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
                Level = DiagnosticLevel.High,
                Title = "🟠 断言失败或主动硬编码断点触发 (Assert Failure / DebugBreak - 0x80000003)",
                Description = "目标程序命中了软件断点指令 (int 3 / __debugbreak)。这通常由代码中 assert(condition) 表达式为假、调用了 DebugBreak()、或在无调试器附加的环境下触发了调试断言引发。",
                Module = rootCauseFrame?.ModuleName ?? triggerFrame?.ModuleName ?? "Unknown",
                Function = rootCauseFrame?.FunctionName ?? triggerFrame?.FunctionName ?? "Unknown",
                SourceFile = rootCauseFrame?.SourceFile ?? triggerFrame?.SourceFile ?? string.Empty,
                Line = rootCauseFrame?.LineNumber ?? triggerFrame?.LineNumber ?? 0,
                Confidence = 0.95
            };

            finding.Evidence.Add($"异常代码: 0x{exp.ExceptionCode:X8} (STATUS_BREAKPOINT)");
            finding.Evidence.Add($"断点指令地址: 0x{exp.ExceptionAddress:X16}");
            finding.Evidence.Add($"受影响线程: TID {exp.ThreadId}");

            if (rootCauseFrame != null)
            {
                finding.Evidence.Add($"断言触发业务位置: {rootCauseFrame.ModuleName}!{rootCauseFrame.FunctionName}");
                if (!string.IsNullOrEmpty(rootCauseFrame.SourceFile))
                {
                    finding.Evidence.Add($"源码文件: {rootCauseFrame.SourceFile} 第 {rootCauseFrame.LineNumber} 行");
                }
            }

            finding.Recommendations.Add("排查断言所在行的前置判断条件，检查传入参数或内部状态是否违背了设计契约 (Design by Contract)。");
            finding.Recommendations.Add("确认发布版本 (Release) 是否未定义 NDEBUG 宏，导致调试期的 assert 断言代码被意外编译进生产环境。");

            if (crashThread?.Frames != null)
            {
                finding.RelatedStack.AddRange(crashThread.Frames.Take(10));
            }

            yield return finding;
        }
    }
}
