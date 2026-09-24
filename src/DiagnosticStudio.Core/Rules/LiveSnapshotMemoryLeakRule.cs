using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 运行中热快照与潜在内存泄漏诊断指导规则 (Live Snapshot & Memory Leak Diagnostic Rule)
    /// </summary>
    public sealed class LiveSnapshotMemoryLeakRule : IDiagnosticRule
    {
        /// <summary>
        /// 规则唯一标识名称
        /// </summary>
        public string RuleName => "LiveSnapshotMemoryLeak";

        /// <summary>
        /// 规则描述
        /// </summary>
        public string Description => "识别对运行中进程热捕获的无异常转储，并针对时序监控中发现的潜在内存泄漏提供快照差分对比 (Dump Diff) 指导。";

        /// <summary>
        /// 对 Dump 报告执行诊断规则分析
        /// </summary>
        /// <param name="dumpReport">Dump 分析数据模型</param>
        /// <returns>发现的问题与建议集合</returns>
        public IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport)
        {
            if (dumpReport == null)
            {
                yield break;
            }

            // 若存在明确的未处理崩溃异常 (如 AccessViolation, StackOverflow 等)，由专门的崩溃规则处理
            if (dumpReport.Exception != null && dumpReport.Exception.ExceptionCode != 0)
            {
                yield break;
            }

            // 判断是否为运行中捕获的热快照转储 (如路径包含 Snapshots 或文件名包含 Process_ 或无崩溃异常)
            string fileName = Path.GetFileName(dumpReport.DumpPath);
            bool isLiveSnapshot = fileName.Contains("Process_", StringComparison.OrdinalIgnoreCase) ||
                                  dumpReport.DumpPath.Contains("Snapshot", StringComparison.OrdinalIgnoreCase) ||
                                  (dumpReport.Exception == null && dumpReport.Threads.Count > 0);

            if (isLiveSnapshot)
            {
                var finding = new DiagnosticFinding
                {
                    Level = DiagnosticLevel.Info,
                    Title = "💡 运行中热快照诊断 (Live Process Snapshot Analysis)",
                    Description = "目标转储为运行中即时抓取的热快照（非崩溃异常退出），当前未检测到未处理的硬件/SEH 崩溃异常。\n" +
                                  "若您在【📈 进程监控】页面中发现私有内存持续单调增长 (Memory Leak Trend Alert) 告警，请注意：单一静态快照仅代表某一瞬间的内存状态，无法直接判定哪些内存属于业务缓存、哪些属于泄漏。需通过前后两份快照进行增量对比！",
                    Module = dumpReport.Modules.FirstOrDefault()?.ModuleName ?? "Process",
                    Function = "LiveSnapshot",
                    SourceFile = string.Empty,
                    Line = 0,
                    Confidence = 0.95
                };

                finding.Evidence.Add($"转储文件: {fileName} (大小: {dumpReport.FileSizeDisplay})");
                finding.Evidence.Add($"运行中活跃线程: 共 {dumpReport.Threads.Count} 条");
                finding.Evidence.Add($"系统加载模块: 共 {dumpReport.Modules.Count} 个");

                finding.Recommendations.Add("【步骤 1】将当前快照保留为【基线快照 (Dump A)】。");
                finding.Recommendations.Add("【步骤 2】保持目标程序继续运行，在【📈 进程监控】中观察到私有内存继续增长（如再增 20MB~100MB）后，再次点击【📸 抓取快照并分析】生成【对比快照 (Dump B)】。");
                finding.Recommendations.Add("【步骤 3】点击左侧导航【⚖️ 快照对比 (Diff)】或顶部【⚖️ 双 Dump 对比】，系统将自动差分比对两者在各模块、内存提交段与线程调用堆栈上的增量差异，精准锁定泄漏源头。");

                yield return finding;
            }
        }
    }
}