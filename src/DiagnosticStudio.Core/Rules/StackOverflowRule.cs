using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 栈溢出与深层死递归分析规则 (Stack Overflow & Infinite Recursion)
    /// </summary>
    public sealed class StackOverflowRule : IDiagnosticRule
    {
        public string RuleName => "StackOverflow";

        public string Description => "检测由于无限递归、过大栈局部数组或栈保护页耗尽引发的栈溢出。";

        public IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport)
        {
            if (dumpReport == null)
            {
                yield break;
            }

            bool isExplicitStackOverflow = dumpReport.Exception != null && dumpReport.Exception.ExceptionCode == 0xC00000FD;

            foreach (var thread in dumpReport.Threads)
            {
                if (thread.Frames == null || thread.Frames.Count < 5)
                {
                    continue;
                }

                // 统计函数调用重复度
                var funcGroups = thread.Frames
                    .Where(f => !string.IsNullOrEmpty(f.FunctionName))
                    .GroupBy(f => $"{f.ModuleName}!{f.FunctionName}")
                    .Where(g => g.Count() >= 15)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();

                if (isExplicitStackOverflow && (thread.IsCrashThread || thread.Index == 0) || funcGroups != null)
                {
                    var repeatFrame = funcGroups?.FirstOrDefault() ?? thread.Frames.FirstOrDefault();

                    var finding = new DiagnosticFinding
                    {
                        Level = DiagnosticLevel.Critical,
                        Title = "🔴 栈溢出与深层递归死循环 (Stack Overflow / Infinite Recursion)",
                        Description = funcGroups != null
                            ? $"检测到线程 TID {thread.ThreadId} 中函数 [{funcGroups.Key}] 在调用栈中连续重复调用高达 {funcGroups.Count()} 次，存在严重的无限递归或调用死循环，已耗尽线程栈空间（默认 1MB）。"
                            : $"检测到明确的系统栈溢出异常 STATUS_STACK_OVERFLOW (0xC00000FD)，线程 TID {thread.ThreadId} 的栈指针已撞击栈保护页 (Guard Page)。",
                        Module = repeatFrame?.ModuleName ?? "Unknown",
                        Function = repeatFrame?.FunctionName ?? "Unknown",
                        SourceFile = repeatFrame?.SourceFile ?? string.Empty,
                        Line = repeatFrame?.LineNumber ?? 0,
                        Confidence = 0.98
                    };

                    finding.Evidence.Add($"异常代码: 0xC00000FD (STATUS_STACK_OVERFLOW)");
                    finding.Evidence.Add($"受影响线程: TID {thread.ThreadId}");
                    if (funcGroups != null)
                    {
                        finding.Evidence.Add($"递归函数: {funcGroups.Key} (调用栈内出现 {funcGroups.Count()} 次)");
                    }

                    finding.Recommendations.Add("检查递归函数的终止边界条件 (Base Case)，确认是否在特定入参下无法满足返回条件。");
                    finding.Recommendations.Add("排查是否存在相互调用的间接死循环（如 FunctionA -> FunctionB -> FunctionA）。");
                    finding.Recommendations.Add("排查是否在函数栈上声明了超大局部变量（例如 char buffer[2 * 1024 * 1024]），超出了 Windows 默认线程栈大小，建议改用堆分配 (std::vector / new / malloc)。");

                    finding.RelatedStack.AddRange(thread.Frames.Take(20));

                    yield return finding;
                    break;
                }
            }
        }
    }
}
