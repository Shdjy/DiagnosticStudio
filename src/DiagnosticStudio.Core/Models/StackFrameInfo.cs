namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 调用栈单层栈帧模型
    /// </summary>
    public sealed class StackFrameInfo
    {
        /// <summary>
        /// 指令指针地址 (RIP/EIP)
        /// </summary>
        public ulong InstructionOffset { get; set; }

        /// <summary>
        /// 函数返回地址
        /// </summary>
        public ulong ReturnOffset { get; set; }

        /// <summary>
        /// 栈基址 (RBP/EBP)
        /// </summary>
        public ulong FrameOffset { get; set; }

        /// <summary>
        /// 栈顶指针 (RSP/ESP)
        /// </summary>
        public ulong StackOffset { get; set; }

        /// <summary>
        /// 所在模块名称 (如 ntdll.dll, MyService.dll)
        /// </summary>
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>
        /// 函数签名符号 (如 MyClass::ProcessFrame)
        /// </summary>
        public string FunctionName { get; set; } = string.Empty;

        /// <summary>
        /// 源代码文件完整路径
        /// </summary>
        public string SourceFile { get; set; } = string.Empty;

        /// <summary>
        /// 源代码所在行号 (1-based)
        /// </summary>
        public uint LineNumber { get; set; }

        /// <summary>
        /// 指令相对于函数符号的偏移量
        /// </summary>
        public uint Displacement { get; set; }

        /// <summary>
        /// 是否属于用户业务代码（非 Windows 核心系统库）
        /// </summary>
        public bool IsUserCode { get; set; }

        /// <summary>
        /// 是否为推导出的用户责任崩溃根因行 (Root Cause Blame)
        /// </summary>
        public bool IsRootCause { get; set; }

        /// <summary>
        /// UI 高亮级别（用于在源码和栈帧视图中自动标红或标黄）
        /// </summary>
        public DiagnosticLevel? HighlightLevel { get; set; }

        /// <summary>
        /// 格式化为可读的单行调用栈文本
        /// </summary>
        public override string ToString()
        {
            if (!string.IsNullOrEmpty(SourceFile) && LineNumber > 0)
            {
                return $"{ModuleName}!{FunctionName} [{SourceFile}:{LineNumber}]";
            }

            if (!string.IsNullOrEmpty(FunctionName))
            {
                return $"{ModuleName}!{FunctionName}+0x{Displacement:X}";
            }

            return $"{ModuleName}+0x{InstructionOffset:X}";
        }
    }
}
