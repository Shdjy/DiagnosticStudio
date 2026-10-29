using System.Collections.Generic;

namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 转储文件物理完整性级别
    /// </summary>
    public enum DumpIntegrityStatus
    {
        /// <summary>
        /// 完整无损：所有核心流与目录完整有效
        /// </summary>
        Complete,

        /// <summary>
        /// 部分可用：部分流缺失或部分线程栈截断，但仍可读取核心上下文
        /// </summary>
        PartiallyAvailable,

        /// <summary>
        /// 启发式恢复：原文件流目录缺失或截断，已通过物理签名启发式自愈恢复
        /// </summary>
        HeuristicallyRecovered,

        /// <summary>
        /// 损坏严重：关键流损坏且无法可靠解析
        /// </summary>
        Corrupted
    }

    /// <summary>
    /// Dump 转储文件完整性与各流质量报告
    /// </summary>
    public sealed class DumpIntegrityInfo
    {
        /// <summary>
        /// 整体完整性状态
        /// </summary>
        public DumpIntegrityStatus Status { get; set; } = DumpIntegrityStatus.Complete;

        /// <summary>
        /// 系统信息流状态 (SystemInfoStream)
        /// </summary>
        public bool HasSystemInfoStream { get; set; } = true;

        /// <summary>
        /// 异常记录流状态 (ExceptionStream)
        /// </summary>
        public bool HasExceptionStream { get; set; } = true;

        /// <summary>
        /// 模块列表流状态 (ModuleListStream)
        /// </summary>
        public bool HasModuleListStream { get; set; } = true;

        /// <summary>
        /// 线程列表流状态 (ThreadListStream)
        /// </summary>
        public bool HasThreadListStream { get; set; } = true;

        /// <summary>
        /// 内存描述流状态 (MemoryList / Memory64List)
        /// </summary>
        public bool HasMemoryStream { get; set; } = true;

        /// <summary>
        /// 启发式恢复说明与依据
        /// </summary>
        public List<string> RecoveryNotes { get; set; } = new List<string>();

        /// <summary>
        /// 缺失或受损的流警告列表
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// 格式化状态徽章描述
        /// </summary>
        public string StatusBadge => Status switch
        {
            DumpIntegrityStatus.Complete => "✅ 完整可靠",
            DumpIntegrityStatus.PartiallyAvailable => "⚠️ 部分可用 (部分流缺失)",
            DumpIntegrityStatus.HeuristicallyRecovered => "🛠️ 启发式容错恢复 (存在结构推测)",
            _ => "❌ 损坏无法可靠解析"
        };
    }
}
