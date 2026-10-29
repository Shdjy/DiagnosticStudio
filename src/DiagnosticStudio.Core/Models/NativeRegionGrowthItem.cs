using System;
using DiagnosticStudio.Core.Storage;

namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 虚拟内存区域差分项 (Virtual Memory Region Diff)
    /// 说明：本项反映进程虚拟内存空间 (VirtualAlloc / mmap) 的物理提交变动，
    /// 明确区分操作系统虚拟内存区域变动与具体单个堆对象 (malloc/new) 分配。
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
        /// 虚拟内存区域结束地址 (BaseAddress + RegionSize)
        /// </summary>
        public ulong EndAddress => BaseAddress + (ulong)Math.Max(0, SizeB);

        /// <summary>
        /// Dump A 中的区域物理提交大小 (字节)
        /// </summary>
        public long SizeA { get; set; }

        /// <summary>
        /// Dump B 中的区域物理提交大小 (字节)
        /// </summary>
        public long SizeB { get; set; }

        /// <summary>
        /// 区域增量字节数 (SizeB - SizeA)
        /// </summary>
        public long DeltaBytes => SizeB - SizeA;

        /// <summary>
        /// 内存状态变动类型 (新增分配段 / 扩容段 / 缩小 / 保持 / 释放重分配)
        /// </summary>
        public string GrowthStatus { get; set; } = "新增分配段";

        /// <summary>
        /// 页面状态 (如 MEM_COMMIT=0x1000, MEM_RESERVE=0x2000)
        /// </summary>
        public uint State { get; set; } = 0x1000;

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

        /// <summary>
        /// 是否与前一 Dump 中的内存区域存在地址区间重叠（重叠合并/拆分分析）
        /// </summary>
        public bool IsOverlapped { get; set; }

        /// <summary>
        /// 语义解释说明：提醒用户此为虚拟内存段粒度，非单一对象
        /// </summary>
        public string SemanticNotice { get; set; } = "虚拟内存提交区域变动（不等价于单个 new/malloc 对象）";

        // UI 格式化展示属性
        public string SizeADisplay => StorageCleanupSummary.FormatBytes(SizeA);
        public string SizeBDisplay => StorageCleanupSummary.FormatBytes(SizeB);
        public string DeltaSizeDisplay => (DeltaBytes > 0 ? "+" : "") + StorageCleanupSummary.FormatBytes(DeltaBytes);
    }
}
