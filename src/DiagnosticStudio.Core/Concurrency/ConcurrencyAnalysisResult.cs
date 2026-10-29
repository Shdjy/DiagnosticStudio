using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Concurrency
{
    /// <summary>
    /// 全局线程并发、等待图与死锁分析综合结果
    /// </summary>
    public sealed class ConcurrencyAnalysisResult
    {
        /// <summary>
        /// 检测到的死锁环路列表
        /// </summary>
        public List<DeadlockCycle> Deadlocks { get; set; } = new List<DeadlockCycle>();

        /// <summary>
        /// 所有处于阻塞或等待态的线程节点分析列表
        /// </summary>
        public List<ThreadWaitNode> BlockedThreads { get; set; } = new List<ThreadWaitNode>();

        /// <summary>
        /// 是否疑似出现线程池工作线程耗尽 (ThreadPool Starvation)
        /// </summary>
        public bool IsThreadPoolExhausted { get; set; }

        /// <summary>
        /// 处于同步阻塞状态的线程总数
        /// </summary>
        public int TotalBlockedThreadCount { get; set; }

        /// <summary>
        /// 并发分析推导出的诊断项
        /// </summary>
        public List<DiagnosticFinding> Findings { get; set; } = new List<DiagnosticFinding>();
    }
}
