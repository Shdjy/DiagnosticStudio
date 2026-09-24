using System;
using System.Diagnostics;
using System.IO;
using DiagnosticStudio.Core.Storage;

namespace DiagnosticStudio.Core.Logging
{
    /// <summary>
    /// 标准诊断系统分层日志记录器
    /// </summary>
    public sealed class DiagnosticLogger : IDiagnosticLogger
    {
        public static DiagnosticLogger Instance { get; } = new DiagnosticLogger();

        /// <summary>
        /// 当产生新日志时触发，方便 UI 日志窗口实时绑定
        /// </summary>
        public event Action<string>? OnLogMessage;

        public void Log(LogLevel level, string module, string message, Exception? ex = null)
        {
            string timeStr = DateTime.Now.ToString("HH:mm:ss.fff");
            int threadId = Environment.CurrentManagedThreadId;
            string exMsg = ex != null ? $" | 异常: {ex.GetType().Name}: {ex.Message}" : string.Empty;
            string formatted = $"[{timeStr}] [TID:{threadId:D2}] [{level.ToString().ToUpper()}] [{module}] {message}{exMsg}";

            Console.WriteLine(formatted);
            OnLogMessage?.Invoke(formatted);

            try
            {
                string logFile = WorkspaceStorageService.Instance.GetCurrentLogFilePath();
                File.AppendAllText(logFile, formatted + Environment.NewLine);
            }
            catch
            {
            }
        }

        public void Info(string module, string message) => Log(LogLevel.Info, module, message);

        public void Warning(string module, string message) => Log(LogLevel.Warning, module, message);

        public void Error(string module, string message, Exception? ex = null) => Log(LogLevel.Error, module, message, ex);

        public IDisposable Measure(string module, string operationName)
        {
            return new MeasureScope(this, module, operationName);
        }

        private sealed class MeasureScope : IDisposable
        {
            private readonly DiagnosticLogger _logger;
            private readonly string _module;
            private readonly string _operation;
            private readonly Stopwatch _stopwatch;

            public MeasureScope(DiagnosticLogger logger, string module, string operation)
            {
                _logger = logger;
                _module = module;
                _operation = operation;
                _stopwatch = Stopwatch.StartNew();
                _logger.Info(_module, $"开始执行: {_operation}...");
            }

            public void Dispose()
            {
                _stopwatch.Stop();
                _logger.Info(_module, $"完成执行: {_operation}，耗时 {_stopwatch.ElapsedMilliseconds} ms");
            }
        }
    }
}