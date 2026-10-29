using System;
using System.Collections.Generic;

namespace DiagnosticStudio.Core.Industrial
{
    /// <summary>
    /// 工业时序事件采集记录器接口
    /// 面向工业检测 7x24 高频运行设计，具备无 GC 压力、容量上限轮转与极速诊断快照导出能力
    /// </summary>
    public interface IIndustrialTimelineRecorder
    {
        /// <summary>
        /// 记录一条工业事件
        /// </summary>
        void RecordEvent(IndustrialTimelineEvent ev);

        /// <summary>
        /// 便捷记录事件重载
        /// </summary>
        void Record(
            string moduleName,
            IndustrialEventType eventType,
            long frameId = 0,
            string taskId = "",
            double durationMs = 0,
            int statusCode = 0,
            string? errorMessage = null);

        /// <summary>
        /// 导出当前缓冲区内的所有有效时序事件（按发生时序排列）
        /// </summary>
        IReadOnlyList<IndustrialTimelineEvent> GetAllEvents();

        /// <summary>
        /// 获取距离当前最近指定时间窗口内的时序事件切片
        /// </summary>
        IReadOnlyList<IndustrialTimelineEvent> GetRecentEvents(TimeSpan window);

        /// <summary>
        /// 清空所有事件记录
        /// </summary>
        void Clear();

        /// <summary>
        /// 缓冲区当前记录总数
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 缓冲区最大容量上限
        /// </summary>
        int Capacity { get; }
    }
}
