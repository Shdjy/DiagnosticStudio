using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 主线程阻塞与潜在卡死/挂起分析规则 (Hang & Deadlock Analysis Rule)
    /// </summary>
    public sealed class HangOrDeadlockRule : IDiagnosticRule
    {
        public string RuleName => "HangOrDeadlock";

        public string Description => "检测无崩溃异常的手动转储中，主线程或关键业务线程是否长期阻塞在同步等待、互斥锁或系统调用中。";

        public IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport)
        {
            if (dumpReport == null)
            {
                yield break;
            }

            // 仅对未发生崩溃退出的 Dump (如现场主动抓取的卡死转储) 执行阻塞诊断
            if (dumpReport.Exception != null && dumpReport.Exception.ExceptionCode != 0)
            {
                yield break;
            }

            if (dumpReport.Threads == null || dumpReport.Threads.Count == 0)
            {
                yield break;
            }

            // 获取主线程 (通常为 TID 最小或 Index=0 的线程)
            var mainThread = dumpReport.Threads.FirstOrDefault(t => t.Index == 0)
                          ?? dumpReport.Threads.FirstOrDefault();

            if (mainThread?.Frames == null || mainThread.Frames.Count == 0)
            {
                yield break;
            }

            // 检查主线程栈顶是否处于典型的等待函数中
            var waitKeywords = new[] { "wait", "sleep", "ntwait", "criticalsection", "lock", "ke_wait", "msgwait" };
            var topFrame = mainThread.Frames.FirstOrDefault();
            bool isWaiting = mainThread.Frames.Take(3).Any(f =>
                waitKeywords.Any(k => f.FunctionName.ToLowerInvariant().Contains(k)));

            if (isWaiting)
            {
                var userFrame = mainThread.Frames.FirstOrDefault(f => f.IsUserCode) ?? topFrame;

                var finding = new DiagnosticFinding
                {
                    Level = DiagnosticLevel.Warning,
                    Title = "🟡 线程同步等待与界面疑似卡死挂起 (Potential Hang / Lock Contention)",
                    Description = $"目标程序在捕获转储时主线程 (TID {mainThread.ThreadId}) 处于同步等待或锁阻塞状态 ({topFrame?.FunctionName})。如果是由于界面无响应抓取的 Dump，表明主线程可能遭遇死锁、高耗时同步阻塞或未响应的消息循环。",
                    Module = userFrame?.ModuleName ?? "Unknown",
                    Function = userFrame?.FunctionName ?? "Unknown",
                    SourceFile = userFrame?.SourceFile ?? string.Empty,
                    Line = userFrame?.LineNumber ?? 0,
                    Confidence = 0.80
                };

                finding.Evidence.Add($"目标线程: TID {mainThread.ThreadId} (主线程/界面线程)");
                finding.Evidence.Add($"当前阻塞函数: {topFrame?.ModuleName}!{topFrame?.FunctionName}");

                if (userFrame != null && userFrame != topFrame)
                {
                    finding.Evidence.Add($"触发阻塞的用户代码: {userFrame.ModuleName}!{userFrame.FunctionName}");
                    if (!string.IsNullOrEmpty(userFrame.SourceFile))
                    {
                        finding.Evidence.Add($"源码位置: {userFrame.SourceFile} 第 {userFrame.LineNumber} 行");
                    }
                }

                finding.Recommendations.Add("避免在主线程/UI 线程中直接调用同步等待 API（如 WaitForSingleObject、Task.Wait()、.Result）。");
                finding.Recommendations.Add("排查是否存在 AB-BA 交叉锁顺序导致的死锁 (Deadlock)。");
                finding.Recommendations.Add("建议将耗时文件 I/O、网络请求及复杂数据处理移至后台工作线程 (Task.Run 或 std::thread)。");

                finding.RelatedStack.AddRange(mainThread.Frames.Take(10));

                yield return finding;
            }
        }
    }
}
