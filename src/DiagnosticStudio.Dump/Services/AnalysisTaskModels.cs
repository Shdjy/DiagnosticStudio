using System;

namespace DiagnosticStudio.Dump.Services
{
    /// <summary>
    /// 分析任务生命周期状态
    /// </summary>
    public enum AnalysisTaskState
    {
        Queued,
        Running,
        Succeeded,
        PartiallySucceeded,
        Failed,
        Cancelled
    }

    /// <summary>
    /// 分析任务进度通知实体
    /// </summary>
    public sealed class AnalysisProgressInfo
    {
        /// <summary>
        /// 当前阶段描述 (如 "正在加载符号", "正在执行规则评估")
        /// </summary>
        public string StageDescription { get; set; } = string.Empty;

        /// <summary>
        /// 进度百分比 (0 ~ 100)
        /// </summary>
        public int Percentage { get; set; }

        /// <summary>
        /// 当前耗时（毫秒）
        /// </summary>
        public double ElapsedMs { get; set; }
    }

    /// <summary>
    /// 分析任务资源防护限制配置
    /// </summary>
    public sealed class AnalysisResourceLimits
    {
        /// <summary>
        /// Dump 最大允许分析的文件大小（默认 8GB）
        /// </summary>
        public long MaxFileSizeBytes { get; set; } = 8L * 1024 * 1024 * 1024;

        /// <summary>
        /// 单次分析硬超时时间（默认 120 秒）
        /// </summary>
        public TimeSpan AnalysisTimeout { get; set; } = TimeSpan.FromSeconds(120);

        /// <summary>
        /// 最大并发分析任务数（默认 2 个，防止 DbgHelp 底层句柄争用）
        /// </summary>
        public int MaxConcurrentAnalyses { get; set; } = 2;
    }
}
