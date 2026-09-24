using System.Collections.Generic;

namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 转储异常详细记录
    /// </summary>
    public sealed class ExceptionInfo
    {
        /// <summary>
        /// 异常代码 (如 0xC0000005)
        /// </summary>
        public uint ExceptionCode { get; set; }

        /// <summary>
        /// 异常标志位
        /// </summary>
        public uint ExceptionFlags { get; set; }

        /// <summary>
        /// 触发异常的指令地址
        /// </summary>
        public ulong ExceptionAddress { get; set; }

        /// <summary>
        /// 发生异常的线程 ID
        /// </summary>
        public uint ThreadId { get; set; }

        /// <summary>
        /// 异常参数列表 (如 Access Violation 的读写标志与目标内存地址)
        /// </summary>
        public List<ulong> Parameters { get; set; } = new List<ulong>();

        /// <summary>
        /// 人类可读的标准异常名称
        /// </summary>
        public string ExceptionName => ExceptionCode switch
        {
            0xC0000005 => "STATUS_ACCESS_VIOLATION (内存访问冲突)",
            0xC00000FD => "STATUS_STACK_OVERFLOW (栈溢出)",
            0xC000001D => "STATUS_ILLEGAL_INSTRUCTION (非法指令)",
            0xC0000094 => "STATUS_INTEGER_DIVIDE_BY_ZERO (整数被零除)",
            0xC0000025 => "STATUS_NONCONTINUABLE_EXCEPTION (不可恢复异常)",
            0x80000003 => "STATUS_BREAKPOINT (断点触发)",
            0xE0434352 => "CLR_EXCEPTION (.NET CLR 托管异常)",
            _ => $"0x{ExceptionCode:X8}"
        };

        /// <summary>
        /// 获取访问冲突的目标内存地址（仅在 0xC0000005 下有效）
        /// </summary>
        public ulong? TargetMemoryAddress => (ExceptionCode == 0xC0000005 && Parameters.Count >= 2) ? Parameters[1] : null;

        /// <summary>
        /// 获取访问类型 (读/写/执行)
        /// </summary>
        public string AccessType => (ExceptionCode == 0xC0000005 && Parameters.Count >= 1) ? Parameters[0] switch
        {
            0 => "Read (读取)",
            1 => "Write (写入)",
            8 => "Execute (执行保护)",
            _ => "Unknown"
        } : "N/A";
    }
}
