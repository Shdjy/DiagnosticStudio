using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Monitor.Services
{
    /// <summary>
    /// 泄漏趋势智能检测器接口
    /// </summary>
    public interface ILeakTrendDetector
    {
        /// <summary>
        /// 对给定的指标时间序列执行趋势分析并输出预警项
        /// </summary>
        /// <param name="history">采样历史数据</param>
        /// <param name="processId">监控的进程 PID</param>
        /// <returns>检测出的泄漏风险结论（若无则返回空）</returns>
        List<DiagnosticFinding> DetectLeakTrends(IReadOnlyList<MemoryMetrics> history, int processId);
    }
}
