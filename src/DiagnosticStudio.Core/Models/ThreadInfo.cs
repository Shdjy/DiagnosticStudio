using System.Collections.Generic;

namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 进程线程详情模型
    /// </summary>
    public sealed class ThreadInfo
    {
        /// <summary>
        /// 操作系统线程唯一标识 (TID)
        /// </summary>
        public uint ThreadId { get; set; }

        /// <summary>
        /// 线程在转储中的索引编号
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 是否为发生崩溃/异常的故障线程
        /// </summary>
        public bool IsCrashThread { get; set; }

        /// <summary>
        /// 线程调用栈帧集合
        /// </summary>
        public List<StackFrameInfo> Frames { get; set; } = new List<StackFrameInfo>();

        /// <summary>
        /// 栈顶函数概览
        /// </summary>
        public string TopFrameSummary => Frames.Count > 0 ? Frames[0].ToString() : "No Stack";
    }
}
