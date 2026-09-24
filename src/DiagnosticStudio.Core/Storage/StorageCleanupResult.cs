using System;

namespace DiagnosticStudio.Core.Storage
{
    /// <summary>
    /// 诊断工作区存储清理操作结果模型
    /// </summary>
    public sealed class StorageCleanupResult
    {
        /// <summary>
        /// 成功删除的文件数量
        /// </summary>
        public int DeletedFileCount { get; set; }

        /// <summary>
        /// 成功释放的磁盘空间字节数
        /// </summary>
        public long FreedBytes { get; set; }

        /// <summary>
        /// 清理失败或被占用的文件数量
        /// </summary>
        public int FailedFileCount { get; set; }

        /// <summary>
        /// 格式化释放的空间文本显示
        /// </summary>
        public string FreedSizeDisplay => StorageCleanupSummary.FormatBytes(FreedBytes);

        /// <summary>
        /// 清理执行提示或详细说明
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}