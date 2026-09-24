using System;

namespace DiagnosticStudio.Core.Storage
{
    /// <summary>
    /// 诊断工作区存储统计摘要模型
    /// </summary>
    public sealed class StorageCleanupSummary
    {
        /// <summary>
        /// 快照 Dump 文件总数量
        /// </summary>
        public int SnapshotFileCount { get; set; }

        /// <summary>
        /// 快照 Dump 文件占用总字节数
        /// </summary>
        public long SnapshotTotalBytes { get; set; }

        /// <summary>
        /// 系统日志文件总数量
        /// </summary>
        public int LogFileCount { get; set; }

        /// <summary>
        /// 系统日志文件占用总字节数
        /// </summary>
        public long LogTotalBytes { get; set; }

        /// <summary>
        /// 诊断报告与导出临时文件总数量
        /// </summary>
        public int ReportFileCount { get; set; }

        /// <summary>
        /// 诊断报告与导出临时文件占用总字节数
        /// </summary>
        public long ReportTotalBytes { get; set; }

        /// <summary>
        /// 中间文件总数量
        /// </summary>
        public int TotalFileCount => SnapshotFileCount + LogFileCount + ReportFileCount;

        /// <summary>
        /// 中间文件总占用字节数
        /// </summary>
        public long TotalSizeBytes => SnapshotTotalBytes + LogTotalBytes + ReportTotalBytes;

        /// <summary>
        /// 格式化快照占用大小
        /// </summary>
        public string SnapshotSizeDisplay => FormatBytes(SnapshotTotalBytes);

        /// <summary>
        /// 格式化日志占用大小
        /// </summary>
        public string LogSizeDisplay => FormatBytes(LogTotalBytes);

        /// <summary>
        /// 格式化报告占用大小
        /// </summary>
        public string ReportSizeDisplay => FormatBytes(ReportTotalBytes);

        /// <summary>
        /// 格式化总占用大小显示
        /// </summary>
        public string TotalSizeDisplay => FormatBytes(TotalSizeBytes);

        /// <summary>
        /// 字节大小格式化辅助方法
        /// </summary>
        /// <param name="bytes">字节数</param>
        /// <returns>带单位的可读文本</returns>
        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            if (bytes >= 1024L * 1024 * 1024)
            {
                return $"{(double)bytes / (1024 * 1024 * 1024):F2} GB";
            }

            if (bytes >= 1024L * 1024)
            {
                return $"{(double)bytes / (1024 * 1024):F2} MB";
            }

            if (bytes >= 1024)
            {
                return $"{(double)bytes / 1024:F2} KB";
            }

            return $"{bytes} B";
        }
    }
}