using System;

namespace DiagnosticStudio.Monitor.Services
{
    /// <summary>
    /// 资源与内存增长模式分类
    /// </summary>
    public enum GrowthPatternType
    {
        /// <summary>
        /// 稳定状态（无显著增长或回落）
        /// </summary>
        Stable,

        /// <summary>
        /// 持续单调增长（疑似典型内存泄漏）
        /// </summary>
        SustainedGrowth,

        /// <summary>
        /// 阶梯式增长（阶段性批量加载或缓存分配）
        /// </summary>
        StepGrowth,

        /// <summary>
        /// 周期性波动（如定时心跳、定期垃圾回收或批处理任务）
        /// </summary>
        PeriodicFluctuation,

        /// <summary>
        /// 瞬时尖峰后已回落（非泄漏，正常突发峰值）
        /// </summary>
        TransientSpike,

        /// <summary>
        /// 启动初期缓存预热后趋于平稳（非泄漏）
        /// </summary>
        WarmupStabilized
    }

    /// <summary>
    /// 泄漏趋势智能检测器配置选项
    /// 支持动态配置窗口长度、采样阈值、R² 拟合优度与告警冷却
    /// </summary>
    public sealed class LeakTrendDetectorOptions
    {
        /// <summary>
        /// 执行统计分析所需的最少采样点数（默认 10）
        /// </summary>
        public int MinSamples { get; set; } = 10;

        /// <summary>
        /// 滑动窗口分析的最大点数（默认 60 点）
        /// </summary>
        public int WindowSize { get; set; } = 60;

        /// <summary>
        /// 私有内存每分钟增长报警阈值（字节/分钟，默认 3.0 MB/min）
        /// </summary>
        public double MemoryGrowthThresholdBytesPerMin { get; set; } = 3.0 * 1024 * 1024;

        /// <summary>
        /// 私有内存窗口内绝对净增长报警下限（默认 15 MB）
        /// </summary>
        public long MemoryMinNetGrowthBytes { get; set; } = 15 * 1024 * 1024;

        /// <summary>
        /// 判定为持续泄漏所需的最小判定系数 (R² 拟合优度，0.0 ~ 1.0，默认 0.65)
        /// 过滤大幅波动与离散杂音
        /// </summary>
        public double MinGoodnessOfFitR2 { get; set; } = 0.65;

        /// <summary>
        /// 判定为单调增长所需的最小上升点比例 (默认 0.60 即 60% 以上采样点单调递增)
        /// </summary>
        public double MinMonotonicRatio { get; set; } = 0.60;

        /// <summary>
        /// GDI 句柄绝对告警硬上限（默认 8000 个）
        /// </summary>
        public int GdiWarningThreshold { get; set; } = 8000;

        /// <summary>
        /// GDI 句柄紧急上限（默认 9000 个）
        /// </summary>
        public int GdiCriticalThreshold { get; set; } = 9000;

        /// <summary>
        /// GDI 句柄每秒增长斜率报警阈值（默认 0.5 个/秒）
        /// </summary>
        public double GdiSlopeThresholdPerSec { get; set; } = 0.5;

        /// <summary>
        /// 系统句柄每秒增长斜率报警阈值（默认 2.0 个/秒）
        /// </summary>
        public double HandleSlopeThresholdPerSec { get; set; } = 2.0;

        /// <summary>
        /// 系统句柄窗口内净增告警阈值（默认 100 个）
        /// </summary>
        public int HandleNetGrowthThreshold { get; set; } = 100;

        /// <summary>
        /// 告警冷却间隔秒数（防止在阈值上下频繁反复触发抖动，默认 30 秒）
        /// </summary>
        public int AlertCooldownSeconds { get; set; } = 30;

        /// <summary>
        /// 状态滞回因子 (Hysteresis Factor, 默认 0.85，恢复需降至阈值的 85% 以下)
        /// </summary>
        public double HysteresisFactor { get; set; } = 0.85;
    }
}
