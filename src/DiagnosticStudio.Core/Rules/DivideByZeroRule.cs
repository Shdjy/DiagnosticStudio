using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 除以零异常分析规则 (Integer & Float Divide by Zero)
    /// </summary>
    public sealed class DivideByZeroRule : IDiagnosticRule
    {
        public string RuleName => "DivideByZero";

        public string Description => "检测由于整数除以零 (0xC0000094) 或浮点除以零导致的硬件级算术异常。";

        public IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport)
        {
            if (dumpReport?.Exception == null)
            {
                yield break;
            }

            var exp = dumpReport.Exception;
            // 0xC0000094 = STATUS_INTEGER_DIVIDE_BY_ZERO, 0xC000008F = STATUS_FLOAT_DIVIDE_BY_ZERO
            if (exp.ExceptionCode != 0xC0000094 && exp.ExceptionCode != 0xC000008F)
            {
                yield break;
            }

            var crashThread = dumpReport.Threads.FirstOrDefault(t => t.ThreadId == exp.ThreadId)
                           ?? dumpReport.Threads.FirstOrDefault();

            var rootCauseFrame = crashThread?.Frames.FirstOrDefault(f => f.IsRootCause)
                              ?? crashThread?.Frames.FirstOrDefault(f => f.IsUserCode)
                              ?? crashThread?.Frames.FirstOrDefault();

            var finding = new DiagnosticFinding
            {
                Level = DiagnosticLevel.Critical,
                Title = "🔴 致命算术除以零错误 (Divide by Zero - 0xC0000094)",
                Description = "目标程序在执行除法算术运算或取模运算 (idiv/div) 时，除数分母为 0，触发了 CPU 硬件除零故障异常中断。",
                Module = rootCauseFrame?.ModuleName ?? "Unknown",
                Function = rootCauseFrame?.FunctionName ?? "Unknown",
                SourceFile = rootCauseFrame?.SourceFile ?? string.Empty,
                Line = rootCauseFrame?.LineNumber ?? 0,
                Confidence = 0.99
            };

            finding.Evidence.Add($"异常代码: 0x{exp.ExceptionCode:X8} (STATUS_INTEGER_DIVIDE_BY_ZERO)");
            finding.Evidence.Add($"崩溃指令地址: 0x{exp.ExceptionAddress:X16}");
            finding.Evidence.Add($"受影响线程: TID {exp.ThreadId}");

            if (rootCauseFrame != null)
            {
                finding.Evidence.Add($"除零发生代码位置: {rootCauseFrame.ModuleName}!{rootCauseFrame.FunctionName}");
                if (!string.IsNullOrEmpty(rootCauseFrame.SourceFile))
                {
                    finding.Evidence.Add($"源码文件: {rootCauseFrame.SourceFile} 第 {rootCauseFrame.LineNumber} 行");
                }
            }

            finding.Recommendations.Add("检查崩溃行中作为除数或模数的变量/表达式，在执行除法前增加非零校验 (如 if (denominator != 0))。");
            finding.Recommendations.Add("排查除数是否来自外部配置、UI 输入或硬件传感器读取，增加边界值安全防御与默认托底机制。");

            if (crashThread?.Frames != null)
            {
                finding.RelatedStack.AddRange(crashThread.Frames.Take(10));
            }

            yield return finding;
        }
    }
}
