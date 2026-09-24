using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 未捕获 C++ 异常分析规则 (Unhandled C++ Exception, 0xE06D7363)
    /// </summary>
    public sealed class UnhandledCppExceptionRule : IDiagnosticRule
    {
        public string RuleName => "UnhandledCppException";

        public string Description => "检测未被 catch 捕获并穿透至顶层的标准 C++ 异常 (如 std::runtime_error、std::out_of_range 等)。";

        public IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport)
        {
            if (dumpReport?.Exception == null)
            {
                yield break;
            }

            var exp = dumpReport.Exception;
            // 0xE06D7363 = STATUS_CPP_EH_EXCEPTION ('msc' | 0xE0000000)
            if (exp.ExceptionCode != 0xE06D7363)
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
                Title = "🔴 未捕获的标准 C++ 异常 (Unhandled C++ Exception - 0xE06D7363)",
                Description = "目标程序抛出了 C++ 异常（如 throw std::runtime_error），但没有被任何相关的 try-catch 块捕获，导致异常传递至默认未处理异常过滤器 (UnhandledExceptionFilter) 并引发进程终止崩溃。",
                Module = rootCauseFrame?.ModuleName ?? triggerFrame?.ModuleName ?? "Unknown",
                Function = rootCauseFrame?.FunctionName ?? triggerFrame?.FunctionName ?? "Unknown",
                SourceFile = rootCauseFrame?.SourceFile ?? triggerFrame?.SourceFile ?? string.Empty,
                Line = rootCauseFrame?.LineNumber ?? triggerFrame?.LineNumber ?? 0,
                Confidence = 0.95
            };

            finding.Evidence.Add("异常代码: 0xE06D7363 (Microsoft Visual C++ Exception)");
            finding.Evidence.Add($"崩溃指令地址: 0x{exp.ExceptionAddress:X16}");
            finding.Evidence.Add($"抛出异常线程: TID {exp.ThreadId}");

            if (rootCauseFrame != null && rootCauseFrame != triggerFrame)
            {
                finding.Evidence.Add($"业务抛出异常源: {rootCauseFrame.ModuleName}!{rootCauseFrame.FunctionName}");
                if (!string.IsNullOrEmpty(rootCauseFrame.SourceFile))
                {
                    finding.Evidence.Add($"源码位置: {rootCauseFrame.SourceFile} 第 {rootCauseFrame.LineNumber} 行");
                }
            }

            finding.Recommendations.Add("检查业务代码抛出点的前后逻辑，确认是否缺失了 try-catch(const std::exception& ex) 块保护。");
            finding.Recommendations.Add("在顶层业务入口或线程处理循环中增加全局 std::exception 捕获和详细日志记录，防止未捕获异常导致全程序崩溃。");
            finding.Recommendations.Add("排查底层依赖库或驱动通信接口，确认是否返回了导致主动 throw 错误的外部异常数据。");

            if (crashThread?.Frames != null)
            {
                finding.RelatedStack.AddRange(crashThread.Frames.Take(12));
            }

            yield return finding;
        }
    }
}
