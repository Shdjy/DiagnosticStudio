using System;

namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 进程实时运行期关键指标快照
    /// 具备采样耗时度量、质量状态跟踪与 PID 复用实例防混淆能力
    /// </summary>
    public sealed class MemoryMetrics
    {
        /// <summary>
        /// 采样高精度时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// 进程实例全局唯一标识（进程启动时间 + PID 复合标识，防止 PID 复用产生脏数据）
        /// </summary>
        public string ProcessInstanceId { get; set; } = string.Empty;

        /// <summary>
        /// 物理工作集内存 (Working Set) 字节数
        /// </summary>
        public long WorkingSet { get; set; }

        /// <summary>
        /// 私有提交内存 (Private Bytes) 字节数（判断内存泄漏的核心指标）
        /// </summary>
        public long PrivateBytes { get; set; }

        /// <summary>
        /// 进程打开的系统句柄总数
        /// </summary>
        public int HandleCount { get; set; }

        /// <summary>
        /// GDI 图元对象总数 (上限通常为 10,000)
        /// </summary>
        public int GdiObjects { get; set; }

        /// <summary>
        /// USER 窗口对象总数
        /// </summary>
        public int UserObjects { get; set; }

        /// <summary>
        /// 当前活动线程数
        /// </summary>
        public int ThreadCount { get; set; }

        /// <summary>
        /// CPU 占用百分比 (0.0 ~ 100.0)
        /// </summary>
        public double CpuPercentage { get; set; }

        /// <summary>
        /// 本次采样耗时（毫秒）
        /// </summary>
        public double SampleDurationMs { get; set; }

        /// <summary>
        /// 采样是否成功完成
        /// </summary>
        public bool IsSampleSuccessful { get; set; } = true;

        /// <summary>
        /// 若采样失败或降级时的错误详情
        /// </summary>
        public string? SampleErrorMessage { get; set; }

        /// <summary>
        /// 格式化私有内存显示 (MB/GB)
        /// </summary>
        public string PrivateBytesDisplay => PrivateBytes >= 1024L * 1024 * 1024
            ? $"{(double)PrivateBytes / (1024 * 1024 * 1024):F2} GB"
            : $"{(double)PrivateBytes / (1024 * 1024):F2} MB";

        /// <summary>
        /// 格式化工作集内存显示
        /// </summary>
        public string WorkingSetDisplay => WorkingSet >= 1024L * 1024 * 1024
            ? $"{(double)WorkingSet / (1024 * 1024 * 1024):F2} GB"
            : $"{(double)WorkingSet / (1024 * 1024):F2} MB";
    }
}
