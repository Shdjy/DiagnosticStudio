using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticStudio.Core.Logging;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Dump.Services
{
    /// <summary>
    /// 转储分析任务全生命周期与资源限制调度器实现
    /// </summary>
    public sealed class AnalysisTaskManager : IAnalysisTaskManager, IDisposable
    {
        private const string ModuleTag = "AnalysisTaskManager";
        private readonly IDumpAnalyzer _dumpAnalyzer;
        private readonly IDiagnosticLogger _logger;
        private readonly SemaphoreSlim _concurrencyThrottle;
        private readonly AnalysisResourceLimits _limits;

        public AnalysisResourceLimits Limits => _limits;

        public AnalysisTaskManager(
            IDumpAnalyzer? dumpAnalyzer = null,
            AnalysisResourceLimits? limits = null,
            IDiagnosticLogger? logger = null)
        {
            _limits = limits ?? new AnalysisResourceLimits();
            _dumpAnalyzer = dumpAnalyzer ?? new DumpAnalyzerService();
            _logger = logger ?? DiagnosticLogger.Instance;
            _concurrencyThrottle = new SemaphoreSlim(_limits.MaxConcurrentAnalyses, _limits.MaxConcurrentAnalyses);
        }

        public async Task<DumpReportModel> ExecuteAnalysisTaskAsync(
            string dumpFilePath,
            string? symbolPath = null,
            IProgress<AnalysisProgressInfo>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(dumpFilePath))
            {
                throw new ArgumentException("转储文件路径不能为空", nameof(dumpFilePath));
            }

            var fileInfo = new FileInfo(dumpFilePath);
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException($"指定转储文件不存在: {dumpFilePath}", dumpFilePath);
            }

            // 1. 资源防护：大文件与物理配额拦截
            if (fileInfo.Length > _limits.MaxFileSizeBytes)
            {
                throw new InvalidOperationException($"转储文件大小 ({(double)fileInfo.Length / (1024 * 1024 * 1024):F2} GB) 超出系统允许的最大安全上限 ({(double)_limits.MaxFileSizeBytes / (1024 * 1024 * 1024):F2} GB)。");
            }

            // 2. 超时与取消令牌组合
            using var timeoutCts = new CancellationTokenSource(_limits.AnalysisTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var sw = Stopwatch.StartNew();
            progress?.Report(new AnalysisProgressInfo
            {
                StageDescription = "分析任务已排队，正在等待调度资源...",
                Percentage = 5,
                ElapsedMs = sw.ElapsedMilliseconds
            });

            // 3. 并发节流进入
            await _concurrencyThrottle.WaitAsync(linkedCts.Token);
            try
            {
                progress?.Report(new AnalysisProgressInfo
                {
                    StageDescription = "正在打开 Native 转储会话并初始化符号路径...",
                    Percentage = 20,
                    ElapsedMs = sw.ElapsedMilliseconds
                });

                linkedCts.Token.ThrowIfCancellationRequested();

                progress?.Report(new AnalysisProgressInfo
                {
                    StageDescription = "正在解析模块、线程堆栈与崩溃异常记录...",
                    Percentage = 50,
                    ElapsedMs = sw.ElapsedMilliseconds
                });

                var report = await _dumpAnalyzer.AnalyzeAsync(dumpFilePath, symbolPath, linkedCts.Token);

                progress?.Report(new AnalysisProgressInfo
                {
                    StageDescription = "多维诊断规则与完整性评估已完成！",
                    Percentage = 100,
                    ElapsedMs = sw.ElapsedMilliseconds
                });

                _logger.Info(ModuleTag, $"分析任务成功完成: {Path.GetFileName(dumpFilePath)}，总耗时 {sw.ElapsedMilliseconds}ms");
                return report;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.Error(ModuleTag, $"分析任务超时被强行终止 (阈值: {_limits.AnalysisTimeout.TotalSeconds}秒): {dumpFilePath}");
                throw new TimeoutException($"转储分析任务在 {_limits.AnalysisTimeout.TotalSeconds} 秒内未完成，已触发超时保护。");
            }
            catch (Exception ex)
            {
                _logger.Error(ModuleTag, $"分析任务异常失败: {ex.Message}", ex);
                throw;
            }
            finally
            {
                _concurrencyThrottle.Release();
            }
        }

        public void Dispose()
        {
            _concurrencyThrottle.Dispose();
        }
    }
}
