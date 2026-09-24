using System;
using DiagnosticStudio.Core.Storage;

namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 原生虚拟内存段 / 原生堆块增量差分项 (Native Heap / Memory Region Growth)
    /// </summary>
    public sealed class NativeRegionGrowthItem
    {
        /// <summary>
        /// 虚拟内存起始基地址 (十六进制字符串，如 0x000001B482100000)
        /// </summary>
        public string BaseAddressHex { get; set; } = string.Empty;

        /// <summary>
        /// 内存基址整数值
        /// </summary>
        public ulong BaseAddress { get; set; }

        /// <summary>
        /// Dump A 中的段提交大小 (字节)
        /// </summary>
        public long SizeA { get; set; }

        /// <summary>
        /// Dump B 中的段提交大小 (字节)
        /// </summary>
        public long SizeB { get; set; }

        /// <summary>
        /// 段增量字节数 (SizeB - SizeA)
        /// </summary>
        public long DeltaBytes => SizeB - SizeA;

        /// <summary>
        /// 内存状态变动 (新增分配段 / 扩容段 / 保持)
        /// </summary>
        public string GrowthStatus { get; set; } = "新增分配段";

        /// <summary>
        /// 保护属性描述 (PAGE_READWRITE / PAGE_EXECUTE_READWRITE)
        /// </summary>
        public string ProtectDescription { get; set; } = "PAGE_READWRITE";

        /// <summary>
        /// 内存类型描述 (MEM_PRIVATE / MEM_MAPPED / MEM_IMAGE)
        /// </summary>
        public string TypeDescription { get; set; } = "MEM_PRIVATE (原生堆/私有页)";

        /// <summary>
        /// 推导内存用途分类 (原生堆块缓冲区 malloc/new、线程调用栈、图像帧缓冲区等)
        /// </summary>
        public string SuspectCategory { get; set; } = "原生堆内存缓冲区";

        /// <summary>
        /// 关联的线程 ID 或模块名称
        /// </summary>
        public string RelatedOwner { get; set; } = string.Empty;

        // UI 格式化展示属性
        public string SizeADisplay => StorageCleanupSummary.FormatBytes(SizeA);
        public string SizeBDisplay => StorageCleanupSummary.FormatBytes(SizeB);
        public string DeltaSizeDisplay => (DeltaBytes > 0 ? "+" : "") + StorageCleanupSummary.FormatBytes(DeltaBytes);
    }
}
