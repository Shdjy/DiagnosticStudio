namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 诊断问题严重度级别定义
    /// </summary>
    public enum DiagnosticLevel
    {
        /// <summary>
        /// 普通信息（蓝色）
        /// </summary>
        Info,

        /// <summary>
        /// 警告/可能可疑（黄色）
        /// </summary>
        Warning,

        /// <summary>
        /// 高概率问题/业务入口（橙色）
        /// </summary>
        High,

        /// <summary>
        /// 致命崩溃/空指针/越界（红色）
        /// </summary>
        Critical
    }
}
