using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Memory.Managed
{
    /// <summary>
    /// 托管堆 (Managed Heap) 诊断与差分分析器接口
    /// 基于 Microsoft.Diagnostics.Runtime (ClrMD) 深入分析 .NET GC 堆
    /// </summary>
    public interface IManagedHeapAnalyzer
    {
        /// <summary>
        /// 检测目标 Dump 文件是否包含 .NET 托管运行时 (CLR / CoreCLR)
        /// </summary>
        /// <param name="dumpPath">转储文件完整路径</param>
        /// <returns>若为托管进程转储返回 true，否则返回 false</returns>
        bool IsManagedDump(string dumpPath);

        /// <summary>
        /// 差分比对基线 Dump A 与当前 Dump B 的托管堆对象分布
        /// </summary>
        /// <param name="dumpPathA">基线 Dump 文件路径</param>
        /// <param name="dumpPathB">当前 Dump 文件路径</param>
        /// <returns>托管堆对象增量排行列表 (按增量字节数降序)</returns>
        List<HeapObjectGrowthItem> CompareHeaps(string dumpPathA, string dumpPathB);
    }
}
