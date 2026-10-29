using System;
using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Industrial
{
    /// <summary>
    /// 单帧全生命周期追踪链
    /// </summary>
    public sealed class FrameLifecycleTrace
    {
        public long FrameId { get; set; }
        public string TaskId { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public double TotalPipelineDurationMs { get; set; }
        public List<IndustrialTimelineEvent> Events { get; set; } = new List<IndustrialTimelineEvent>();
        public bool HasError => Events.Exists(e => e.StatusCode != 0 || !string.IsNullOrEmpty(e.ErrorMessage));
        public string ErrorSummary => string.Join("; ", Events.FindAll(e => e.StatusCode != 0).ConvertAll(e => $"[{e.ModuleName}] {e.ErrorMessage}"));
    }

    /// <summary>
    /// 工业时序跨来源关联分析引擎接口
    /// 将业务事件、Dump 异常点与资源采样对齐在统一时间线上
    /// </summary>
    public interface ITimelineCorrelator
    {
        /// <summary>
        /// 按 FrameId 关联全流程事件，提取各工件检测生命周期追踪链
        /// </summary>
        IReadOnlyList<FrameLifecycleTrace> TraceFrames(IReadOnlyList<IndustrialTimelineEvent> events);

        /// <summary>
        /// 综合分析业务事件流并产出结构化诊断项（检测算法超时、PLC响应迟滞、积压等）
        /// </summary>
        List<DiagnosticFinding> CorrelateAndDiagnose(
            IReadOnlyList<IndustrialTimelineEvent> events,
            DateTime? crashTime = null,
            double maxExpectedAlgoDurationMs = 100.0);
    }
}
