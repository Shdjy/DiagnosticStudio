using System;
using System.Collections.Generic;

namespace DiagnosticStudio.Core.Industrial
{
    /// <summary>
    /// 工业时序事件通用类型枚举
    /// </summary>
    public enum IndustrialEventType
    {
        Custom,
        TriggerReceived,        // PLC 外部光电/编码器触发信号到达
        ImageAcquired,          // 工业相机完成曝光并回传图像帧
        AlgoStart,              // 缺陷检测/定位算法开始推理
        AlgoCompleted,          // 算法推理完成并输出判定结果
        SignalRejected,         // 向 PLC / 气缸发出剔废或良品信号
        ImageSaved,             // 图像与检测结果异步持久化到磁盘
        PlcHeartbeatTimeout,    // PLC 通信握手超时
        QueueBacklogged,        // 生产队列或图像缓存积压预警
        DeviceError             // 硬件驱动或外设通信故障
    }

    /// <summary>
    /// 工业检测上位机运行期时序事件模型
    /// 统一关联相机曝光、算法推理、PLC交互、剔废落盘等全链路业务
    /// </summary>
    public sealed class IndustrialTimelineEvent
    {
        /// <summary>
        /// 全局自增单调事件序列号
        /// </summary>
        public long SequenceNumber { get; set; }

        /// <summary>
        /// 高精度单调时钟纳秒时间戳 (Stopwatch.GetTimestamp)
        /// </summary>
        public long MonotonicTimestampNs { get; set; }

        /// <summary>
        /// 墙上时钟标准时间
        /// </summary>
        public DateTime WallClockTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 产生事件的进程 ID
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// 产生事件的线程 ID
        /// </summary>
        public int ThreadId { get; set; }

        /// <summary>
        /// 工位编号 (如 Station_1)
        /// </summary>
        public string StationId { get; set; } = string.Empty;

        /// <summary>
        /// 相机标识 (如 Cam_Top, Cam_Side)
        /// </summary>
        public string CameraId { get; set; } = string.Empty;

        /// <summary>
        /// 关键业务图像帧序号 / 触发序列号 (Frame ID)
        /// </summary>
        public long FrameId { get; set; }

        /// <summary>
        /// 业务工件条码 / 批次任务号 (Workpiece / Task ID)
        /// </summary>
        public string TaskId { get; set; } = string.Empty;

        /// <summary>
        /// 责任模块名称 (如 HikCameraModule, PlcDriver, AIVisionEngine)
        /// </summary>
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>
        /// 事件业务类型
        /// </summary>
        public IndustrialEventType EventType { get; set; } = IndustrialEventType.Custom;

        /// <summary>
        /// 自定义事件标签名称
        /// </summary>
        public string EventName { get; set; } = string.Empty;

        /// <summary>
        /// 该环节耗时（毫秒）
        /// </summary>
        public double DurationMs { get; set; }

        /// <summary>
        /// 状态码 (0 表示成功，非 0 为故障码)
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// 故障或异常详细描述
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 附加上下文属性字典
        /// </summary>
        public Dictionary<string, string> Payload { get; set; } = new Dictionary<string, string>();

        public override string ToString()
        {
            return $"[{WallClockTime:HH:mm:ss.fff}] [Frame:{FrameId}] [{ModuleName}] {EventType} ({DurationMs:F1}ms) Status={StatusCode}";
        }
    }
}
