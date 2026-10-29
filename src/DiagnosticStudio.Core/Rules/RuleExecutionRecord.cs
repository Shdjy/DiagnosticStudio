using System;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 规则执行状态枚举
    /// </summary>
    public enum RuleExecutionStatus
    {
        /// <summary>
        /// 规则执行成功完成
        /// </summary>
        Success,

        /// <summary>
        /// 规则执行失败抛出异常（异常被记录并隔离）
        /// </summary>
        Failed,

        /// <summary>
        /// 前置条件不满足，规则被跳过
        /// </summary>
        Skipped
    }

    /// <summary>
    /// 全局分析整体状态枚举
    /// </summary>
    public enum AnalysisOverallStatus
    {
        /// <summary>
        /// 全部分析规则与流水线均成功执行
        /// </summary>
        Success,

        /// <summary>
        /// 部分规则异常或部分流缺失，已降级完成分析
        /// </summary>
        PartialSuccess,

        /// <summary>
        /// 分析流水线因致命异常失败
        /// </summary>
        Failed
    }

    /// <summary>
    /// 诊断规则执行审计与度量记录
    /// 彻底杜绝规则异常静默被吞，提供透明可审计的执行报告
    /// </summary>
    public sealed class RuleExecutionRecord
    {
        /// <summary>
        /// 规则唯一标识
        /// </summary>
        public string RuleId { get; set; } = string.Empty;

        /// <summary>
        /// 规则名称
        /// </summary>
        public string RuleName { get; set; } = string.Empty;

        /// <summary>
        /// 规则版本号
        /// </summary>
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// 执行结果状态
        /// </summary>
        public RuleExecutionStatus Status { get; set; } = RuleExecutionStatus.Success;

        /// <summary>
        /// 执行耗时（毫秒）
        /// </summary>
        public double ElapsedMilliseconds { get; set; }

        /// <summary>
        /// 发现的条目数量
        /// </summary>
        public int FindingsCount { get; set; }

        /// <summary>
        /// 异常类型名称（若发生失败）
        /// </summary>
        public string? ExceptionType { get; set; }

        /// <summary>
        /// 异常详细消息（若发生失败）
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 异常堆栈信息（若发生失败）
        /// </summary>
        public string? StackTrace { get; set; }

        /// <summary>
        /// 跳过执行的原因（若被跳过）
        /// </summary>
        public string? SkipReason { get; set; }
    }
}
