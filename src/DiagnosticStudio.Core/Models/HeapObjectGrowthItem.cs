using System;
using DiagnosticStudio.Core.Storage;

namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 托管堆对象增量差分项 (Managed Heap Type Growth)
    /// </summary>
    public sealed class HeapObjectGrowthItem
    {
        /// <summary>
        /// 托管类型全限定名称 (例如 System.Byte[], System.String, MyNamespace.DataBuffer)
        /// </summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>
        /// Dump A 中的对象存活实例数量
        /// </summary>
        public long CountA { get; set; }

        /// <summary>
        /// Dump B 中的对象存活实例数量
        /// </summary>
        public long CountB { get; set; }

        /// <summary>
        /// 对象数量增量 (CountB - CountA)
        /// </summary>
        public long DeltaCount => CountB - CountA;

        /// <summary>
        /// Dump A 中该类型占用的总堆字节数
        /// </summary>
        public long SizeA { get; set; }

        /// <summary>
        /// Dump B 中该类型占用的总堆字节数
        /// </summary>
        public long SizeB { get; set; }

        /// <summary>
        /// 堆体积增量字节数 (SizeB - SizeA)
        /// </summary>
        public long DeltaBytes => SizeB - SizeA;

        /// <summary>
        /// 对象类别推导 (例如 字节数组缓冲区、字符串池、集合容器、业务实体)
        /// </summary>
        public string Category { get; set; } = "托管对象";

        /// <summary>
        /// 疑似持有根或生命周期特征说明
        /// </summary>
        public string SuspectRootReason { get; set; } = string.Empty;

        // UI 格式化展示属性
        public string SizeADisplay => StorageCleanupSummary.FormatBytes(SizeA);
        public string SizeBDisplay => StorageCleanupSummary.FormatBytes(SizeB);
        public string DeltaCountDisplay => (DeltaCount > 0 ? "+" : "") + DeltaCount.ToString("N0");
        public string DeltaSizeDisplay => (DeltaBytes > 0 ? "+" : "") + StorageCleanupSummary.FormatBytes(DeltaBytes);
    }
}
