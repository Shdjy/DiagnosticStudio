using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Memory.Native
{
    /// <summary>
    /// 原生虚拟内存段与堆分配差分分析器接口
    /// 解析 MiniDump 中的内存段信息流，定位物理内存增量分布与疑似代码泄漏源
    /// </summary>
    public interface INativeMemoryAnalyzer
    {
        /// <summary>
        /// 差分比对基线 Dump A 与当前 Dump B 的原生虚拟内存提交段
        /// </summary>
        /// <param name="dumpPathA">基线转储路径</param>
        /// <param name="dumpPathB">当前转储路径</param>
        /// <param name="threadsB">当前转储中的活动线程列表</param>
        /// <returns>原生内存段增量排行列表</returns>
        List<NativeRegionGrowthItem> CompareNativeRegions(string dumpPathA, string dumpPathB, IReadOnlyList<ThreadInfo> threadsB);

        /// <summary>
        /// 综合线程调用栈、栈帧特征与内存增量，精准定位代码中的疑似泄漏点
        /// </summary>
        /// <param name="reportA">基线 Dump 诊断报告</param>
        /// <param name="reportB">当前 Dump 诊断报告</param>
        /// <param name="deltaBytes">双快照物理体积差量</param>
        /// <returns>疑似泄漏代码定位列表 (按置信度降序排序)</returns>
        List<SuspectCodeLocation> LocateSuspectCode(DumpReportModel reportA, DumpReportModel reportB, long deltaBytes);
    }
}
