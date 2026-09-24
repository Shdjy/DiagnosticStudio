using System;
using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Monitor.Services
{
    /// <summary>
    /// 运行中进程实时性能指标监控器接口
    /// </summary>
    public interface ILiveProcessMonitor : IDisposable
    {
        /// <summary>
        /// 当前是否正在监控
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// 当前监控的目标进程 PID
        /// </summary>
        int TargetProcessId { get; }

        /// <summary>
        /// 当采集到最新一帧监控数据时触发
        /// </summary>
        event Action<MemoryMetrics> OnMetricsSampled;

        /// <summary>
        /// 当检测到潜在泄漏趋势警报时触发
        /// </summary>
        event Action<DiagnosticFinding> OnLeakAlert;

        /// <summary>
        /// 启动对指定进程的实时采样监控
        /// </summary>
        /// <param name="processId">目标进程 PID</param>
        /// <param name="interval">采样周期间隔 (默认 1 秒)</param>
        void StartMonitoring(int processId, TimeSpan? interval = null);

        /// <summary>
        /// 停止当前监控
        /// </summary>
        void StopMonitoring();

        /// <summary>
        /// 获取所有历史采样记录
        /// </summary>
        IReadOnlyList<MemoryMetrics> GetHistory();

        /// <summary>
        /// 列出本机当前运行的所有可用进程列表 (PID, 名称, 标题, 内存)
        /// </summary>
        List<ProcessPickerItem> GetRunningProcesses();
    }

    /// <summary>
    /// 进程选择器条目模型
    /// </summary>
    public sealed class ProcessPickerItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string MainWindowTitle { get; set; } = string.Empty;
        public long WorkingSet { get; set; }
        public string DisplayName => $"[{Id}] {Name} {(string.IsNullOrEmpty(MainWindowTitle) ? "" : $"({MainWindowTitle})")}";
    }
}
