using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Concurrency
{
    /// <summary>
    /// 线程并发、死锁与等待关系分析器接口
    /// </summary>
    public interface IThreadConcurrencyAnalyzer
    {
        /// <summary>
        /// 对单 Dump 中的全部线程进行阻塞分析、等待图构建与死锁闭环检测
        /// </summary>
        ConcurrencyAnalysisResult AnalyzeThreads(IReadOnlyList<ThreadInfo> threads);

        /// <summary>
        /// 对比两个 Dump 之间的线程调用栈，识别长期卡死停滞的线程 (Stuck Threads)
        /// </summary>
        List<ThreadInfo> DetectStuckThreads(IReadOnlyList<ThreadInfo> threadsA, IReadOnlyList<ThreadInfo> threadsB);
    }
}
