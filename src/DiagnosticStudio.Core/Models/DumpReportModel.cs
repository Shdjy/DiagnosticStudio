using System;
using System.Collections.Generic;

namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// Dump 转储文件全面分析报告模型
    /// </summary>
    public sealed class DumpReportModel
    {
        /// <summary>
        /// Dump 文件完整物理路径
        /// </summary>
        public string DumpPath { get; set; } = string.Empty;

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        public long FileSizeBytes { get; set; }

        /// <summary>
        /// 分析生成时间
        /// </summary>
        public DateTime AnalysisTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 操作系统与处理器信息
        /// </summary>
        public ProcessSystemInfo SystemInfo { get; set; } = new ProcessSystemInfo();

        /// <summary>
        /// 崩溃异常记录（可能为空，如手动抓取的正常快照）
        /// </summary>
        public ExceptionInfo? Exception { get; set; }

        /// <summary>
        /// 所有线程列表
        /// </summary>
        public List<ThreadInfo> Threads { get; set; } = new List<ThreadInfo>();

        /// <summary>
        /// 所有加载模块列表
        /// </summary>
        public List<ModuleInfo> Modules { get; set; } = new List<ModuleInfo>();

        /// <summary>
        /// 规则引擎评估生成的全部问题项
        /// </summary>
        public List<DiagnosticFinding> Findings { get; set; } = new List<DiagnosticFinding>();

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        public string FileSizeDisplay => FileSizeBytes >= 1024L * 1024 * 1024
            ? $"{(double)FileSizeBytes / (1024 * 1024 * 1024):F2} GB"
            : $"{(double)FileSizeBytes / (1024 * 1024):F2} MB";
    }
}
