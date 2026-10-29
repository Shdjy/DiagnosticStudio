using System;
using System.Collections.Generic;
using DiagnosticStudio.Core.Rules;

namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// Dump 转储文件全面分析报告模型
    /// 包含 Dump 完整性、符号质量、规则审计轨迹与结构化分析结论
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
        /// 分析引擎版本
        /// </summary>
        public string AnalyzerVersion { get; set; } = "2.1.0";

        /// <summary>
        /// 规则集引擎版本
        /// </summary>
        public string RuleEngineVersion { get; set; } = "2.1.0";

        /// <summary>
        /// 全局分析整体状态 (Success / PartialSuccess / Failed)
        /// </summary>
        public AnalysisOverallStatus OverallStatus { get; set; } = AnalysisOverallStatus.Success;

        /// <summary>
        /// 转储文件物理完整性与质量评估
        /// </summary>
        public DumpIntegrityInfo Integrity { get; set; } = new DumpIntegrityInfo();

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
        /// 规则执行详细审计记录列表（透明呈现每条规则耗时与是否失败）
        /// </summary>
        public List<RuleExecutionRecord> RuleExecutions { get; set; } = new List<RuleExecutionRecord>();

        /// <summary>
        /// 是否通过启发式引擎成功进行了容错修复（流目录未回写或损坏）
        /// </summary>
        public bool IsRecovered { get; set; }

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        public string FileSizeDisplay => FileSizeBytes >= 1024L * 1024 * 1024
            ? $"{(double)FileSizeBytes / (1024 * 1024 * 1024):F2} GB"
            : $"{(double)FileSizeBytes / (1024 * 1024):F2} MB";
    }
}
