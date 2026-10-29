using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using DiagnosticStudio.Core.Logging;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Monitor.Services
{
    /// <summary>
    /// 进程监控状态枚举
    /// </summary>
    public enum MonitorState
    {
        Stopped,
        Starting,
        Running,
        Faulted,
        Disposed
    }

    /// <summary>
    /// 运行中进程无侵入实时性能采样与泄漏监测器实现
    /// 具备明确状态机转换、高精度耗时度量、采样质量追踪与 PID 复用防护
    /// </summary>
    public sealed class LiveProcessMonitor : ILiveProcessMonitor
    {
        private readonly ILeakTrendDetector _leakDetector;
        private readonly IDiagnosticLogger _logger;
        private readonly List<MemoryMetrics> _history = new List<MemoryMetrics>();
        private readonly object _lock = new object();

        private Timer? _sampleTimer;
        private IntPtr _targetProcessHandle = IntPtr.Zero;
        private Process? _targetProcess;
        private string _processInstanceId = string.Empty;
        private DateTime _lastCpuSampleTime;
        private TimeSpan _lastCpuTotalTime;
        private volatile MonitorState _state = MonitorState.Stopped;

        public bool IsRunning => _state == MonitorState.Running;
        public MonitorState State => _state;
        public int TargetProcessId { get; private set; }

        public event Action<MemoryMetrics>? OnMetricsSampled;
        public event Action<DiagnosticFinding>? OnLeakAlert;

        public LiveProcessMonitor(ILeakTrendDetector? leakDetector = null, IDiagnosticLogger? logger = null)
        {
            _leakDetector = leakDetector ?? new LeakTrendDetector();
            _logger = logger ?? DiagnosticLogger.Instance;
        }

        #region Windows 原生 API 声明
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS_EX
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr hProcess, out PROCESS_MEMORY_COUNTERS_EX counters, uint size);

        [DllImport("user32.dll")]
        private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessHandleCount(IntPtr hProcess, out uint pdwHandleCount);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint GR_GDIOBJECTS = 0;
        private const uint GR_USEROBJECTS = 1;
        #endregion

        public void StartMonitoring(int processId, TimeSpan? interval = null)
        {
            lock (_lock)
            {
                if (_state == MonitorState.Disposed)
                {
                    throw new ObjectDisposedException(nameof(LiveProcessMonitor));
                }

                StopMonitoringInternal();
                _state = MonitorState.Starting;

                TargetProcessId = processId;
                TimeSpan samplePeriod = interval ?? TimeSpan.FromSeconds(1);

                try
                {
                    _targetProcess = Process.GetProcessById(processId);

                    // 基于 PID 与 进程启动时间生成唯一实例标记，防止 PID 复用
                    string startTimeStr = "0";
                    try
                    {
                        startTimeStr = _targetProcess.StartTime.Ticks.ToString();
                    }
                    catch
                    {
                    }
                    _processInstanceId = $"{processId}_{startTimeStr}";

                    _targetProcessHandle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
                    if (_targetProcessHandle == IntPtr.Zero)
                    {
                        _logger.Warning("LiveMonitor", $"以标准权限打开进程 PID {processId} 句柄失败，将回退使用 .NET Process 标准接口采集");
                    }

                    _lastCpuSampleTime = DateTime.UtcNow;
                    try
                    {
                        _lastCpuTotalTime = _targetProcess.TotalProcessorTime;
                    }
                    catch
                    {
                        _lastCpuTotalTime = TimeSpan.Zero;
                    }

                    _history.Clear();
                    _state = MonitorState.Running;
                    _sampleTimer = new Timer(SampleTimerCallback, null, TimeSpan.Zero, samplePeriod);

                    _logger.Info("LiveMonitor", $"开始监控目标进程 PID {processId} (Instance: {_processInstanceId})，采样周期: {samplePeriod.TotalMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    _state = MonitorState.Faulted;
                    _logger.Error("LiveMonitor", $"启动进程监控失败 PID {processId}: {ex.Message}", ex);
                    StopMonitoringInternal();
                    throw;
                }
            }
        }

        public void StopMonitoring()
        {
            lock (_lock)
            {
                StopMonitoringInternal();
            }
        }

        private void StopMonitoringInternal()
        {
            if (_sampleTimer != null)
            {
                _sampleTimer.Dispose();
                _sampleTimer = null;
            }

            if (_targetProcessHandle != IntPtr.Zero)
            {
                CloseHandle(_targetProcessHandle);
                _targetProcessHandle = IntPtr.Zero;
            }

            if (_targetProcess != null)
            {
                _targetProcess.Dispose();
                _targetProcess = null;
            }

            if (_state != MonitorState.Disposed)
            {
                _state = MonitorState.Stopped;
            }

            _logger.Info("LiveMonitor", $"已停止监控进程 PID {TargetProcessId}");
        }

        private void SampleTimerCallback(object? state)
        {
            if (_state != MonitorState.Running)
            {
                return;
            }

            var sw = Stopwatch.StartNew();
            var metrics = new MemoryMetrics
            {
                Timestamp = DateTime.Now,
                ProcessInstanceId = _processInstanceId
            };

            try
            {
                if (_targetProcess == null || _targetProcess.HasExited)
                {
                    _logger.Warning("LiveMonitor", $"目标进程 PID {TargetProcessId} 已退出，自动终止监控");
                    StopMonitoring();
                    return;
                }

                _targetProcess.Refresh();

                // 1. 采集内存数据 (Private Bytes, Working Set)
                if (_targetProcessHandle != IntPtr.Zero)
                {
                    if (GetProcessMemoryInfo(_targetProcessHandle, out var counters, (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX>()))
                    {
                        metrics.WorkingSet = (long)counters.WorkingSetSize.ToUInt64();
                        metrics.PrivateBytes = (long)counters.PrivateUsage.ToUInt64();
                    }
                    else
                    {
                        metrics.WorkingSet = _targetProcess.WorkingSet64;
                        metrics.PrivateBytes = _targetProcess.PagedMemorySize64;
                    }
                }
                else
                {
                    metrics.WorkingSet = _targetProcess.WorkingSet64;
                    metrics.PrivateBytes = _targetProcess.PagedMemorySize64;
                }

                // 2. 采集句柄与 GDI 图元
                if (_targetProcessHandle != IntPtr.Zero)
                {
                    if (GetProcessHandleCount(_targetProcessHandle, out uint handleCount))
                    {
                        metrics.HandleCount = (int)handleCount;
                    }
                    else
                    {
                        metrics.HandleCount = _targetProcess.HandleCount;
                    }

                    metrics.GdiObjects = (int)GetGuiResources(_targetProcessHandle, GR_GDIOBJECTS);
                    metrics.UserObjects = (int)GetGuiResources(_targetProcessHandle, GR_USEROBJECTS);
                }
                else
                {
                    metrics.HandleCount = _targetProcess.HandleCount;
                }

                metrics.ThreadCount = _targetProcess.Threads.Count;

                // 3. 计算 CPU 利用率
                DateTime nowUtc = DateTime.UtcNow;
                try
                {
                    TimeSpan currentCpuTime = _targetProcess.TotalProcessorTime;
                    double cpuDeltaMs = (currentCpuTime - _lastCpuTotalTime).TotalMilliseconds;
                    double wallDeltaMs = (nowUtc - _lastCpuSampleTime).TotalMilliseconds;

                    if (wallDeltaMs > 100)
                    {
                        metrics.CpuPercentage = Math.Max(0.0, Math.Min(100.0, (cpuDeltaMs / (wallDeltaMs * Environment.ProcessorCount)) * 100.0));
                        _lastCpuSampleTime = nowUtc;
                        _lastCpuTotalTime = currentCpuTime;
                    }
                }
                catch
                {
                }

                sw.Stop();
                metrics.SampleDurationMs = sw.Elapsed.TotalMilliseconds;
                metrics.IsSampleSuccessful = true;

                // 4. 追加历史并进行泄漏趋势研判
                List<DiagnosticFinding>? findings = null;
                lock (_lock)
                {
                    _history.Add(metrics);
                    // 仅保留最多 600 个采样点
                    if (_history.Count > 600)
                    {
                        _history.RemoveAt(0);
                    }

                    findings = _leakDetector.DetectLeakTrends(_history, TargetProcessId);
                }

                // 5. 触发外部事件通知
                OnMetricsSampled?.Invoke(metrics);

                if (findings != null && findings.Count > 0)
                {
                    foreach (var finding in findings)
                    {
                        OnLeakAlert?.Invoke(finding);
                    }
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                metrics.SampleDurationMs = sw.Elapsed.TotalMilliseconds;
                metrics.IsSampleSuccessful = false;
                metrics.SampleErrorMessage = ex.Message;
                _logger.Warning("LiveMonitor", $"采样目标进程出现瞬态异常: {ex.Message}");
            }
        }

        public IReadOnlyList<MemoryMetrics> GetHistory()
        {
            lock (_lock)
            {
                return _history.ToList().AsReadOnly();
            }
        }

        public List<ProcessPickerItem> GetRunningProcesses()
        {
            var list = new List<ProcessPickerItem>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    list.Add(new ProcessPickerItem
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                        MainWindowTitle = p.MainWindowTitle,
                        WorkingSet = p.WorkingSet64
                    });
                }
                catch
                {
                    // 权限受限的系统进程跳过
                }
            }

            return list.OrderByDescending(p => p.WorkingSet).ToList();
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _state = MonitorState.Disposed;
                StopMonitoringInternal();
            }
        }
    }
}
