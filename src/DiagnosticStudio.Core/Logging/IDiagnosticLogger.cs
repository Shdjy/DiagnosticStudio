using System;

namespace DiagnosticStudio.Core.Logging
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// 诊断系统日志接口
    /// </summary>
    public interface IDiagnosticLogger
    {
        void Log(LogLevel level, string module, string message, Exception? ex = null);
        void Info(string module, string message);
        void Warning(string module, string message);
        void Error(string module, string message, Exception? ex = null);
        IDisposable Measure(string module, string operationName);
    }
}
