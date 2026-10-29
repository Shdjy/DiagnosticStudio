using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Concurrency
{
    /// <summary>
    /// 线程等待同步原语类型
    /// </summary>
    public enum WaitPrimitiveType
    {
        /// <summary>
        /// 无明显阻塞或处于普通运行态
        /// </summary>
        None,

        /// <summary>
        /// 临界区 / 互斥锁 (CriticalSection / Mutex / Monitor.Enter)
        /// </summary>
        CriticalSection,

        /// <summary>
        /// 事件 / 信号量 (Event / Semaphore / Task.Wait)
        /// </summary>
        EventOrTask,

        /// <summary>
        /// 多对象等待 (WaitForMultipleObjects)
        /// </summary>
        MultipleObjects,

        /// <summary>
        /// 睡眠 / 定时等待 (Sleep / Delay)
        /// </summary>
        Sleep,

        /// <summary>
        /// 线程池工作线程等待新任务 (GetQueuedCompletionStatus)
        /// </summary>
        ThreadPoolWorkerWait,

        /// <summary>
        /// 未知同步原语
        /// </summary>
        Unknown
    }

    /// <summary>
    /// 线程同步与等待状态分析节点
    /// </summary>
    public sealed class ThreadWaitNode
    {
        /// <summary>
        /// 线程 ID
        /// </summary>
        public uint ThreadId { get; set; }

        /// <summary>
        /// 线程在 Dump 中的序号
        /// </summary>
        public int ThreadIndex { get; set; }

        /// <summary>
        /// 等待的同步原语类型
        /// </summary>
        public WaitPrimitiveType WaitType { get; set; } = WaitPrimitiveType.None;

        /// <summary>
        /// 等待的目标资源描述（如 0x000001B482100000 或 同步对象名称）
        /// </summary>
        public string TargetResource { get; set; } = string.Empty;

        /// <summary>
        /// 关键阻塞等待栈帧摘要
        /// </summary>
        public string BlockingFrameSummary { get; set; } = string.Empty;

        /// <summary>
        /// 该线程正在等待的目标持有者线程 ID（若可推导）
        /// </summary>
        public uint? WaitingForThreadId { get; set; }

        /// <summary>
        /// 该线程持有的同步资源描述列表
        /// </summary>
        public List<string> OwnedResources { get; set; } = new List<string>();

        /// <summary>
        /// 是否处于活动阻塞等待状态
        /// </summary>
        public bool IsBlocked => WaitType != WaitPrimitiveType.None && WaitType != WaitPrimitiveType.ThreadPoolWorkerWait;
    }
}
