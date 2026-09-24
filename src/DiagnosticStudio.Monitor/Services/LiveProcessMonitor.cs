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
    /// 运行中进程无侵入实时性能采样与泄漏监测器实现
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
        private DateTime _lastCpuSampleTime;
        private TimeSpan _lastCpuTotalTime;

        public bool IsRunning { get; private set; }
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
            StopMonitoring();

            TargetProcessId = processId;
            TimeSpan samplePeriod = interval ?? TimeSpan.FromSeconds(1);

            try
            {
                _targetProcess = Process.GetProcessById(processId);
                _targetProcessHandle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);

                if (_targetProcessHandle == IntPtr.Zero)
                {
                    _logger.Warning("LiveMonitor", $"以标准权限打开进程 PID {processId} 句柄失败，将回退使用 .NET Process 标准接口采集");
                }

                _lastCpuSampleTime = DateTime.UtcNow;
                _lastCpuTotalTime = _targetProcess.TotalProcessorTime;

                lock (_lock)
                {
                    _history.Clear();
                }

                IsRunning = true;
                _sampleTimer = new Timer(OnSampleTimerTick, null, TimeSpan.Zero, samplePeriod);
                _logger.Info("LiveMonitor", $"已启动对目标进程 PID {processId} ({_targetProcess.ProcessName}) 的实时监控，采样频率: {samplePeriod.TotalSeconds}s");
            }
            catch (Exception ex)
            {
                _logger.Error("LiveMonitor", $"启动进程监控失败 PID: {processId}", ex);
                StopMonitoring();
                throw;
            }
        }

        public void StopMonitoring()
        {
            if (!IsRunning && _sampleTimer == null)
            {
                return;
            }

            IsRunning = false;
            _sampleTimer?.Dispose();
            _sampleTimer = null;

            if (_targetProcessHandle != IntPtr.Zero)
            {
                CloseHandle(_targetProcessHandle);
                _targetProcessHandle = IntPtr.Zero;
            }

            _targetProcess?.Dispose();
            _targetProcess = null;

            _logger.Info("LiveMonitor", "实时监控已停止");
        }

        private void OnSampleTimerTick(object? state)
        {
            if (!IsRunning || _targetProcess == null)
            {
                return;
            }

            try
            {
                _targetProcess.Refresh();
                if (_targetProcess.HasExited)
                {
                    _logger.Warning("LiveMonitor", $"目标进程 PID {TargetProcessId} 已退出");
                    StopMonitoring();
                    return;
                }

                var metrics = new MemoryMetrics
                {
                    Timestamp = DateTime.Now,
                    ThreadCount = _targetProcess.Threads.Count
                };

                // 优先通过 Win32 API 高精度采集
                if (_targetProcessHandle != IntPtr.Zero)
                {
                    var counters = new PROCESS_MEMORY_COUNTERS_EX();
                    counters.cb = (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX));
                    if (GetProcessMemoryInfo(_targetProcessHandle, out counters, counters.cb))
                    {
                        metrics.WorkingSet = (long)counters.WorkingSetSize.ToUInt64();
                        metrics.PrivateBytes = (long)counters.PrivateUsage.ToUInt64();
                    }

                    if (GetProcessHandleCount(_targetProcessHandle, out uint hCount))
                    {
                        metrics.HandleCount = (int)hCount;
                    }

                    metrics.GdiObjects = (int)GetGuiResources(_targetProcessHandle, GR_GDIOBJECTS);
                    metrics.UserObjects = (int)GetGuiResources(_targetProcessHandle, GR_USEROBJECTS);
                }
                else
                {
                    // 回退方案
                    metrics.WorkingSet = _targetProcess.WorkingSet64;
                    metrics.PrivateBytes = _targetProcess.PagedMemorySize64;
                    metrics.HandleCount = _targetProcess.HandleCount;
                }

                // CPU 计算
                DateTime nowUtc = DateTime.UtcNow;
                TimeSpan currentCpuTotal = _targetProcess.TotalProcessorTime;
                double elapsedMs = (nowUtc - _lastCpuSampleTime).TotalMilliseconds;
                if (elapsedMs > 0)
                {
                    double cpuUsedMs = (currentCpuTotal - _lastCpuTotalTime).TotalMilliseconds;
                    metrics.CpuPercentage = Math.Max(0.0, Math.Min(100.0, (cpuUsedMs / (elapsedMs * Environment.ProcessorCount)) * 100.0));
                }

                _lastCpuSampleTime = nowUtc;
                _lastCpuTotalTime = currentCpuTotal;

                // 记录历史
                List<MemoryMetrics> snapshotList;
                lock (_lock)
                {
                    _history.Add(metrics);
                    // 保留至多 600 个点（即 10 分钟）
                    if (_history.Count > 600)
                    {
                        _history.RemoveAt(0);
                    }
                    snapshotList = new List<MemoryMetrics>(_history);
                }

                // 派发采样事件
                OnMetricsSampled?.Invoke(metrics);

                // 泄漏趋势识别
                var alerts = _leakDetector.DetectLeakTrends(snapshotList, TargetProcessId);
                foreach (var alert in alerts)
                {
                    OnLeakAlert?.Invoke(alert);
                }
            }
            catch (Exception ex)
            {
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
            StopMonitoring();
        }
    }
}
