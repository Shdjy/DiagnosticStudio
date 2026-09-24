using System.Collections.Generic;
using DiagnosticStudio.Core.Models;
using DiagnosticStudio.Core.Storage;

namespace DiagnosticStudio.Memory.Services
{
    /// <summary>
    /// 模块内存与大小变化差分项
    /// </summary>
    public sealed class ModuleGrowthItem
    {
        public string ModuleName { get; set; } = string.Empty;
        public long SizeA { get; set; }
        public long SizeB { get; set; }
        public long DeltaBytes { get => SizeB - SizeA; set { } }
        public string Status { get; set; } = string.Empty; // 新增 / 卸载 / 相同
        public string SizeADisplay { get => StorageCleanupSummary.FormatBytes(SizeA); set { } }
        public string SizeBDisplay { get => StorageCleanupSummary.FormatBytes(SizeB); set { } }
        public string DeltaDisplay { get => (DeltaBytes > 0 ? "+" : "") + StorageCleanupSummary.FormatBytes(DeltaBytes); set { } }
    }

    /// <summary>
    /// 线程增量变化差分项
    /// </summary>
    public sealed class ThreadGrowthItem
    {
        public uint ThreadId { get; set; }
        public string Status { get; set; } = string.Empty; // 新生线程 / 活跃责任线程 / 已终止 / 保持
        public string TopFrame { get; set; } = string.Empty;
    }

    /// <summary>
    /// 双 Dump 差分对比结果模型 (包含托管堆、原生内存段与源码定位多维结论)
    /// </summary>
    public sealed class DumpDiffResult
    {
        public string DumpPathA { get; set; } = string.Empty;
        public string DumpPathB { get; set; } = string.Empty;

        public long FileSizeA { get; set; }
        public long FileSizeB { get; set; }
        public long DeltaFileSizeBytes { get => FileSizeB - FileSizeA; set { } }

        public string FileSizeADisplay { get => StorageCleanupSummary.FormatBytes(FileSizeA); set { } }
        public string FileSizeBDisplay { get => StorageCleanupSummary.FormatBytes(FileSizeB); set { } }
        public string DeltaFileSizeDisplay { get => (DeltaFileSizeBytes > 0 ? "+" : "") + StorageCleanupSummary.FormatBytes(DeltaFileSizeBytes); set { } }

        public int ThreadCountA { get; set; }
        public int ThreadCountB { get; set; }
        public int DeltaThreadCount { get => ThreadCountB - ThreadCountA; set { } }
        public string DeltaThreadDisplay { get => (DeltaThreadCount > 0 ? "+" : "") + DeltaThreadCount; set { } }

        public int ModuleCountA { get; set; }
        public int ModuleCountB { get; set; }
        public int DeltaModuleCount { get => ModuleCountB - ModuleCountA; set { } }
        public string DeltaModuleDisplay { get => (DeltaModuleCount > 0 ? "+" : "") + DeltaModuleCount; set { } }

        /// <summary>
        /// 是否检测到目标程序运行在 .NET / CLR 托管运行时下
        /// </summary>
        public bool IsDotNetProcess { get; set; }

        /// <summary>
        /// 托管堆对象增长排行列表 (仅当 IsDotNetProcess 为 true 时包含数据)
        /// </summary>
        public List<HeapObjectGrowthItem> TopManagedGrowths { get; set; } = new List<HeapObjectGrowthItem>();

        /// <summary>
        /// 原生虚拟内存段 / 原生堆块增长排行列表
        /// </summary>
        public List<NativeRegionGrowthItem> TopNativeGrowths { get; set; } = new List<NativeRegionGrowthItem>();

        /// <summary>
        /// 疑似代码泄漏点定位列表 (按置信度排序)
        /// </summary>
        public List<SuspectCodeLocation> SuspectLocations { get; set; } = new List<SuspectCodeLocation>();

        /// <summary>
        /// 最优先的主要疑似代码泄漏点
        /// </summary>
        public SuspectCodeLocation? PrimarySuspect { get; set; }

        public bool HasPrimarySuspect { get => PrimarySuspect != null; set { } }
        public string PrimarySuspectDisplay { get => PrimarySuspect?.DisplayLocation ?? "未捕获明确用户代码定位"; set { } }
        public string PrimarySuspectDetail { get => PrimarySuspect?.DiagnosisDetail ?? string.Empty; set { } }

        public List<ModuleGrowthItem> TopModuleGrowths { get; set; } = new List<ModuleGrowthItem>();
        public List<ThreadGrowthItem> ThreadChanges { get; set; } = new List<ThreadGrowthItem>();
        public List<DiagnosticFinding> DiffFindings { get; set; } = new List<DiagnosticFinding>();
    }

    /// <summary>
    /// 双 Dump 对比引擎接口
    /// </summary>
    public interface IDumpComparator
    {
        /// <summary>
        /// 对比基线 Dump A 与当前 Dump B 的指标、托管堆、原生内存段、模块和线程差异
        /// </summary>
        /// <param name="reportA">基线 Dump 诊断报告</param>
        /// <param name="reportB">当前 Dump 诊断报告</param>
        /// <returns>差分对比分析结果</returns>
        DumpDiffResult Compare(DumpReportModel reportA, DumpReportModel reportB);
    }
}