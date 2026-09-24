using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using DiagnosticStudio.Core.Logging;
using DiagnosticStudio.Core.Models;
using DiagnosticStudio.Core.Reports;
using DiagnosticStudio.Dump.Services;
using DiagnosticStudio.Memory.Services;
using DiagnosticStudio.Monitor.Services;
using DiagnosticStudio.Core.Storage;
using Microsoft.Win32;

namespace DiagnosticStudio.UI.ViewModels
{
    /// <summary>
    /// 主界面交互调度核心 ViewModel
    /// </summary>
    public sealed class MainViewModel : ViewModelBase
    {
        private readonly IDumpAnalyzer _dumpAnalyzer;
        private readonly ILiveProcessMonitor _liveMonitor;
        private readonly ILiveSnapshotService _snapshotService;
        private readonly IDumpComparator _comparator;
        private readonly IReportGenerator _htmlReportGenerator;
        private readonly IDiagnosticLogger _logger;
        private readonly IWorkspaceStorageService _storageService;

        private string _storageSummaryDisplay = "统计计算中...";

        public string StorageSummaryDisplay
        {
            get => _storageSummaryDisplay;
            private set => SetProperty(ref _storageSummaryDisplay, value);
        }

        public string StorageDirectoryDisplay => _storageService.RootDataDirectory;

        private int _selectedTabIndex;
        private DumpReportModel? _currentDumpReport;
        private ThreadInfo? _selectedThread;
        private StackFrameInfo? _selectedFrame;
        private DiagnosticFinding? _selectedFinding;
        private ProcessPickerItem? _selectedProcess;
        private DumpDiffResult? _diffResult;
        private string _statusMessage = "就绪 - 请拖入 Dump 文件或选择运行中进程开始诊断";
        private bool _isBusy;
        private string _symbolPath = DumpAnalyzerService.DefaultSymbolServer;

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public DumpReportModel? CurrentDumpReport
        {
            get => _currentDumpReport;
            set
            {
                if (SetProperty(ref _currentDumpReport, value))
                {
                    OnPropertyChanged(nameof(HasDump));
                    OnPropertyChanged(nameof(CriticalCount));
                    OnPropertyChanged(nameof(HighCount));
                    OnPropertyChanged(nameof(WarningCount));
                    OnPropertyChanged(nameof(ModuleCount));
                    OnPropertyChanged(nameof(ThreadCount));
                    OnPropertyChanged(nameof(ExceptionNameDisplay));
                    OnPropertyChanged(nameof(ExceptionAddressDisplay));
                    OnPropertyChanged(nameof(FaultingThreadIdDisplay));
                }
            }
        }

        public bool HasDump => CurrentDumpReport != null;
        public int CriticalCount => CurrentDumpReport?.Findings.Count(f => f.Level == DiagnosticLevel.Critical) ?? 0;
        public int HighCount => CurrentDumpReport?.Findings.Count(f => f.Level == DiagnosticLevel.High) ?? 0;
        public int WarningCount => CurrentDumpReport?.Findings.Count(f => f.Level == DiagnosticLevel.Warning) ?? 0;
        public int ModuleCount => CurrentDumpReport?.Modules.Count ?? 0;
        public int ThreadCount => CurrentDumpReport?.Threads.Count ?? 0;
        public string ExceptionNameDisplay => CurrentDumpReport?.Exception?.ExceptionName ?? "无异常记录";
        public string ExceptionAddressDisplay => CurrentDumpReport?.Exception != null ? $"0x{CurrentDumpReport.Exception.ExceptionAddress:X16}" : "N/A";
        public string FaultingThreadIdDisplay => CurrentDumpReport?.Exception != null ? $"TID {CurrentDumpReport.Exception.ThreadId}" : "N/A";

        public ThreadInfo? SelectedThread
        {
            get => _selectedThread;
            set => SetProperty(ref _selectedThread, value);
        }

        public StackFrameInfo? SelectedFrame
        {
            get => _selectedFrame;
            set => SetProperty(ref _selectedFrame, value);
        }

        public DiagnosticFinding? SelectedFinding
        {
            get => _selectedFinding;
            set => SetProperty(ref _selectedFinding, value);
        }

        public ProcessPickerItem? SelectedProcess
        {
            get => _selectedProcess;
            set => SetProperty(ref _selectedProcess, value);
        }

        public DumpDiffResult? DiffResult
        {
            get => _diffResult;
            set => SetProperty(ref _diffResult, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public string SymbolPath
        {
            get => _symbolPath;
            set => SetProperty(ref _symbolPath, value);
        }

        public ObservableCollection<ProcessPickerItem> ProcessList { get; } = new ObservableCollection<ProcessPickerItem>();
        public ObservableCollection<ProcessPickerItem> FilteredProcessList { get; } = new ObservableCollection<ProcessPickerItem>();
        public ObservableCollection<DiagnosticFinding> MonitorAlerts { get; } = new ObservableCollection<DiagnosticFinding>();
        public ObservableCollection<string> LogConsoleLines { get; } = new ObservableCollection<string>();

        private string _processSearchText = string.Empty;
        private readonly List<ProcessPickerItem> _allProcesses = new List<ProcessPickerItem>();

        /// <summary>
        /// 目标进程筛选关键字 (支持按进程名或进程 ID 实时检索)
        /// </summary>
        public string ProcessSearchText
        {
            get => _processSearchText;
            set
            {
                if (SetProperty(ref _processSearchText, value))
                {
                    ApplyProcessFilter();
                }
            }
        }

        // 源码定位事件（通知 View 层的 SourceCodeViewer 滚动和高亮）
        public event Action<string, uint, DiagnosticLevel, string>? OnNavigateToSourceRequested;
        public event Action<MemoryMetrics>? OnMetricUpdated;

        #region 命令声明
        public ICommand OpenDumpCommand { get; }
        public ICommand RefreshProcessListCommand { get; }
        public ICommand ClearProcessSearchCommand { get; }
        public ICommand StartMonitorCommand { get; }
        public ICommand StopMonitorCommand { get; }
        public ICommand CaptureLiveSnapshotCommand { get; }
        public ICommand ExportHtmlCommand { get; }
        public ICommand NavigateToFindingCommand { get; }
        public ICommand NavigateToFrameCommand { get; }
        public ICommand NavigateToPrimarySuspectCommand { get; }
        public ICommand CompareDumpsCommand { get; }
        public ICommand BrowseSymbolDirectoryCommand { get; }
        public ICommand ResetSymbolPathCommand { get; }
        public ICommand ReanalyzeDumpCommand { get; }
        public ICommand CleanIntermediateFilesCommand { get; }
        public ICommand OpenDataDirectoryCommand { get; }
        public ICommand OpenSnapshotsDirectoryCommand { get; }
        public ICommand RefreshStorageSummaryCommand { get; }
        #endregion

        public MainViewModel(
            IDumpAnalyzer? dumpAnalyzer = null,
            ILiveProcessMonitor? liveMonitor = null,
            ILiveSnapshotService? snapshotService = null,
            IDumpComparator? comparator = null,
            IReportGenerator? htmlReportGenerator = null,
            IDiagnosticLogger? logger = null,
            IWorkspaceStorageService? storageService = null)
        {
            _dumpAnalyzer = dumpAnalyzer ?? new DumpAnalyzerService();
            _liveMonitor = liveMonitor ?? new LiveProcessMonitor();
            _snapshotService = snapshotService ?? new LiveSnapshotService();
            _comparator = comparator ?? new DumpComparator();
            _htmlReportGenerator = htmlReportGenerator ?? new HtmlReportGenerator();
            _logger = logger ?? DiagnosticLogger.Instance;
            _storageService = storageService ?? WorkspaceStorageService.Instance;

            // 监听监控事件
            _liveMonitor.OnMetricsSampled += (metrics) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    OnMetricUpdated?.Invoke(metrics);
                });
            };

            _liveMonitor.OnLeakAlert += (alert) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (!MonitorAlerts.Any(a => a.Title == alert.Title))
                    {
                        MonitorAlerts.Insert(0, alert);
                    }
                });
            };

            // 监听系统日志
            if (_logger is DiagnosticLogger diagLog)
            {
                diagLog.OnLogMessage += (msg) =>
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        LogConsoleLines.Add(msg);
                        if (LogConsoleLines.Count > 200)
                        {
                            LogConsoleLines.RemoveAt(0);
                        }
                    });
                };
            }

            // 初始化命令
            OpenDumpCommand = new RelayCommand(async param => await ExecuteOpenDumpAsync(param as string));
            RefreshProcessListCommand = new RelayCommand(ExecuteRefreshProcessList);
            ClearProcessSearchCommand = new RelayCommand(() => ProcessSearchText = string.Empty);
            StartMonitorCommand = new RelayCommand(ExecuteStartMonitor);
            StopMonitorCommand = new RelayCommand(ExecuteStopMonitor);
            CaptureLiveSnapshotCommand = new RelayCommand(async () => await ExecuteCaptureLiveSnapshotAsync());
            ExportHtmlCommand = new RelayCommand(ExecuteExportHtml);
            NavigateToFindingCommand = new RelayCommand(param => ExecuteNavigateToFinding(param as DiagnosticFinding));
            NavigateToFrameCommand = new RelayCommand(param => ExecuteNavigateToFrame(param as StackFrameInfo));
            NavigateToPrimarySuspectCommand = new RelayCommand(() => ExecuteNavigateToSuspect(DiffResult?.PrimarySuspect));
            CompareDumpsCommand = new RelayCommand(async () => await ExecuteCompareDumpsAsync());
            BrowseSymbolDirectoryCommand = new RelayCommand(ExecuteBrowseSymbolDirectory);
            ResetSymbolPathCommand = new RelayCommand(ExecuteResetSymbolPath);
            ReanalyzeDumpCommand = new RelayCommand(async () => await ExecuteReanalyzeDumpAsync());
            CleanIntermediateFilesCommand = new RelayCommand(ExecuteCleanIntermediateFiles);
            OpenDataDirectoryCommand = new RelayCommand(() => _storageService.OpenDirectoryInExplorer());
            OpenSnapshotsDirectoryCommand = new RelayCommand(() => _storageService.OpenDirectoryInExplorer(_storageService.GetSnapshotsDirectory()));
            RefreshStorageSummaryCommand = new RelayCommand(RefreshStorageSummary);

            ExecuteRefreshProcessList();
            RefreshStorageSummary();
        }

        public async Task LoadDumpFileAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                MessageBox.Show("指定的 Dump 文件路径不存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsBusy = true;
            StatusMessage = $"正在通过 DbgHelp 分析转储: {Path.GetFileName(filePath)}...";

            try
            {
                var report = await _dumpAnalyzer.AnalyzeAsync(filePath, SymbolPath);
                CurrentDumpReport = report;
                SelectedTabIndex = 0; // 切换到 Overview

                if (report.Threads.Count > 0)
                {
                    SelectedThread = report.Threads.FirstOrDefault(t => t.IsCrashThread) ?? report.Threads[0];
                }

                StatusMessage = $"分析就绪: {Path.GetFileName(filePath)} | 发现 {report.Findings.Count} 项诊断建议";

                // 若发现关键崩溃异常，自动在源码视图中定位第一现场
                var topFinding = report.Findings.FirstOrDefault(f => f.Level == DiagnosticLevel.Critical && !string.IsNullOrEmpty(f.SourceFile));
                if (topFinding != null)
                {
                    ExecuteNavigateToFinding(topFinding);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"分析失败: {ex.Message}";
                MessageBox.Show($"解析 Dump 出现严重异常: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExecuteOpenDumpAsync(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                var dialog = new OpenFileDialog
                {
                    Title = "选择 Windows 转储文件 (Crash / Hang Dump)",
                    Filter = "转储文件 (*.dmp)|*.dmp|所有文件 (*.*)|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    await LoadDumpFileAsync(dialog.FileName);
                }
            }
            else
            {
                await LoadDumpFileAsync(path);
            }
        }

        private void ExecuteRefreshProcessList()
        {
            _allProcesses.Clear();
            var list = _liveMonitor.GetRunningProcesses();
            _allProcesses.AddRange(list);

            ApplyProcessFilter();

            if (SelectedProcess == null && FilteredProcessList.Count > 0)
            {
                SelectedProcess = FilteredProcessList.FirstOrDefault(p => p.Name.Contains("CrashTarget", StringComparison.OrdinalIgnoreCase))
                               ?? FilteredProcessList[0];
            }
        }

        private void ApplyProcessFilter()
        {
            FilteredProcessList.Clear();
            string query = _processSearchText.Trim();

            if (string.IsNullOrEmpty(query))
            {
                foreach (var item in _allProcesses)
                {
                    FilteredProcessList.Add(item);
                }
            }
            else
            {
                bool isPidQuery = int.TryParse(query, out int searchPid);

                foreach (var item in _allProcesses)
                {
                    bool match = item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                 (isPidQuery && item.Id == searchPid);

                    if (match)
                    {
                        FilteredProcessList.Add(item);
                    }
                }
            }

            if (SelectedProcess != null && !FilteredProcessList.Contains(SelectedProcess))
            {
                SelectedProcess = FilteredProcessList.FirstOrDefault();
            }
        }

        private void ExecuteStartMonitor()
        {
            if (SelectedProcess == null)
            {
                MessageBox.Show("请先在下拉列表中选择需要监控的目标进程。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MonitorAlerts.Clear();
            _liveMonitor.StartMonitoring(SelectedProcess.Id);
            StatusMessage = $"正在实时监控进程: {SelectedProcess.DisplayName} (采样频率 1000ms)";
        }

        private void ExecuteStopMonitor()
        {
            _liveMonitor.StopMonitoring();
            StatusMessage = "实时指标采集已停止";
        }

        private async Task ExecuteCaptureLiveSnapshotAsync()
        {
            if (SelectedProcess == null)
            {
                MessageBox.Show("请先选择目标运行进程。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsBusy = true;
            StatusMessage = $"正在抓取进程 [{SelectedProcess.DisplayName}] 实时内存转储...";

            try
            {
                string dumpPath = await _snapshotService.CaptureSnapshotAsync(SelectedProcess.Id, SelectedProcess.Name);
                StatusMessage = $"快照生成成功: {Path.GetFileName(dumpPath)}，正在自动挂载诊断...";
                RefreshStorageSummary();
                await LoadDumpFileAsync(dumpPath);
            }
            catch (Exception ex)
            {
                StatusMessage = $"抓取快照失败: {ex.Message}";
                MessageBox.Show($"快照采集异常: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExecuteBrowseSymbolDirectory()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择本地 PDB 符号存放目录",
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                string selectedPath = dialog.FolderName;
                if (!string.IsNullOrWhiteSpace(selectedPath))
                {
                    SymbolPath = $"{selectedPath};{DumpAnalyzerService.DefaultSymbolServer}";
                    StatusMessage = $"本地符号路径已更新: {selectedPath}";
                }
            }
        }

        private void ExecuteResetSymbolPath()
        {
            SymbolPath = DumpAnalyzerService.DefaultSymbolServer;
            StatusMessage = "符号路径已重置为默认微软官方符号服务器";
        }

        private async Task ExecuteReanalyzeDumpAsync()
        {
            if (CurrentDumpReport == null || string.IsNullOrEmpty(CurrentDumpReport.DumpPath))
            {
                MessageBox.Show("当前未载入任何 Dump 文件，请先打开转储文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await LoadDumpFileAsync(CurrentDumpReport.DumpPath);
        }

        private void ExecuteExportHtml()
        {
            if (CurrentDumpReport == null)
            {
                MessageBox.Show("请先加载 Dump 文件后再导出诊断报告。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "导出 HTML 格式专业诊断报告",
                Filter = "HTML 报告 (*.html)|*.html",
                FileName = $"DiagnosticReport_{Path.GetFileNameWithoutExtension(CurrentDumpReport.DumpPath)}_{DateTime.Now:yyyyMMdd_HHmmss}.html"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _htmlReportGenerator.ExportToFile(CurrentDumpReport, dialog.FileName);
                    RefreshStorageSummary();
                    StatusMessage = $"报告已导出至: {dialog.FileName}";
                    var open = MessageBox.Show("诊断报告导出成功！是否立即在默认浏览器中打开预览？", "导出成功", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (open == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = dialog.FileName,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出报告失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public void ExecuteNavigateToFinding(DiagnosticFinding? finding)
        {
            if (finding == null)
            {
                return;
            }

            SelectedFinding = finding;

            if (!string.IsNullOrEmpty(finding.SourceFile) && finding.Line > 0)
            {
                SelectedTabIndex = 2; // 切换到 Source View
                OnNavigateToSourceRequested?.Invoke(finding.SourceFile, finding.Line, finding.Level, finding.Description);
            }
            else
            {
                SelectedTabIndex = 1; // 切换到 Dump View
            }
        }

        public void ExecuteNavigateToFrame(StackFrameInfo? frame)
        {
            if (frame == null)
            {
                return;
            }

            SelectedFrame = frame;

            if (!string.IsNullOrEmpty(frame.SourceFile) && frame.LineNumber > 0)
            {
                SelectedTabIndex = 2; // 切换到 Source View
                DiagnosticLevel level = frame.HighlightLevel ?? (frame.IsUserCode ? DiagnosticLevel.Warning : DiagnosticLevel.Info);
                string tooltip = $"{frame.ModuleName}!{frame.FunctionName} (行 {frame.LineNumber})";
                OnNavigateToSourceRequested?.Invoke(frame.SourceFile, frame.LineNumber, level, tooltip);
            }
        }

        /// <summary>
        /// 精准直达疑似泄漏源源码位置
        /// </summary>
        public void ExecuteNavigateToSuspect(SuspectCodeLocation? suspect)
        {
            if (suspect == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(suspect.SourceFile) && suspect.LineNumber > 0)
            {
                SelectedTabIndex = 2; // 切换到 Source View
                OnNavigateToSourceRequested?.Invoke(
                    suspect.SourceFile,
                    suspect.LineNumber,
                    DiagnosticLevel.Critical,
                    $"{suspect.ModuleName}!{suspect.FunctionName} (行 {suspect.LineNumber})\n{suspect.DiagnosisDetail}");
            }
            else
            {
                SelectedTabIndex = 1; // 切换到 Dump View
            }
        }

        private async Task ExecuteCompareDumpsAsync()
        {
            var dialogA = new OpenFileDialog { Title = "选择基线 Dump A (前一个时间点快照)", Filter = "Dump 文件 (*.dmp)|*.dmp" };
            if (dialogA.ShowDialog() != true) return;

            var dialogB = new OpenFileDialog { Title = "选择对比 Dump B (后一个时间点快照)", Filter = "Dump 文件 (*.dmp)|*.dmp" };
            if (dialogB.ShowDialog() != true) return;

            IsBusy = true;
            StatusMessage = "正在对比双 Dump 数据差异与内存堆分布...";

            try
            {
                var rA = await _dumpAnalyzer.AnalyzeAsync(dialogA.FileName, SymbolPath);
                var rB = await _dumpAnalyzer.AnalyzeAsync(dialogB.FileName, SymbolPath);

                DiffResult = _comparator.Compare(rA, rB);
                SelectedTabIndex = 4; // 切换到 Dump Diff 视图
                StatusMessage = $"双 Dump 对比完成: 净增体积 {(double)DiffResult.DeltaFileSizeBytes / (1024 * 1024):F2} MB | 疑似泄漏责任定位: {DiffResult.PrimarySuspectDisplay}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"对比失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 刷新工作区中间文件统计信息 (快照、日志、临时报告)
        /// </summary>
        public void RefreshStorageSummary()
        {
            try
            {
                var summary = _storageService.GetCleanupSummary();
                StorageSummaryDisplay = $"📸 快照: {summary.SnapshotFileCount} 个 ({summary.SnapshotSizeDisplay})  |  📋 日志: {summary.LogFileCount} 份 ({summary.LogSizeDisplay})  |  📄 临时报告: {summary.ReportFileCount} 份 ({summary.ReportSizeDisplay})";
            }
            catch
            {
                StorageSummaryDisplay = "存储统计刷新失败";
            }
        }

        /// <summary>
        /// 执行中间文件清理操作（快照Dump、历史日志、临时报告）
        /// </summary>
        private void ExecuteCleanIntermediateFiles()
        {
            var summary = _storageService.GetCleanupSummary();
            if (summary.TotalFileCount == 0)
            {
                MessageBox.Show("当前工作区非常干净，暂无可清理的中间文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string confirmMsg = "当前检测到以下可清理的中间数据文件：\n\n" +
                                $"• 快照转储 (Snapshots)：{summary.SnapshotFileCount} 个 ({summary.SnapshotSizeDisplay})\n" +
                                $"• 历史日志与报告：{summary.LogFileCount + summary.ReportFileCount} 个 ({StorageCleanupSummary.FormatBytes(summary.LogTotalBytes + summary.ReportTotalBytes)})\n" +
                                $"总计占用磁盘：{summary.TotalSizeDisplay}\n\n" +
                                "是否确认立即清理上述中间文件以释放磁盘空间？\n(注：当天正在写入的活动日志将受到保护，不会被删除)";

            var result = MessageBox.Show(confirmMsg, "清理中间文件确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                var cleanResult = _storageService.CleanIntermediateFiles(cleanSnapshots: true, cleanLogs: true, cleanReports: true);
                RefreshStorageSummary();
                StatusMessage = $"中间文件清理完成：成功释放空间 {cleanResult.FreedSizeDisplay} (删除 {cleanResult.DeletedFileCount} 个文件)";
                MessageBox.Show(cleanResult.Message, "清理成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
