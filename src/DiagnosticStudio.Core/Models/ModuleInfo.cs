namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 进程加载模块与符号状态模型
    /// </summary>
    public sealed class ModuleInfo
    {
        /// <summary>
        /// 内存基址
        /// </summary>
        public ulong BaseOfImage { get; set; }

        /// <summary>
        /// 模块大小（字节）
        /// </summary>
        public uint SizeOfImage { get; set; }

        /// <summary>
        /// 简短模块名称 (如 kernel32.dll)
        /// </summary>
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>
        /// 二进制完整路径
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// 匹配加载的 PDB 符号文件路径
        /// </summary>
        public string LoadedPdbName { get; set; } = string.Empty;

        /// <summary>
        /// 符号是否成功加载匹配
        /// </summary>
        public bool IsSymbolLoaded { get; set; }

        /// <summary>
        /// 格式化大小显示 (MB/KB)
        /// </summary>
        public string SizeDisplay => SizeOfImage >= 1024 * 1024
            ? $"{(double)SizeOfImage / (1024 * 1024):F2} MB"
            : $"{(double)SizeOfImage / 1024:F2} KB";
    }
}
