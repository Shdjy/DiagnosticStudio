namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 转储目标环境操作系统与处理器信息
    /// </summary>
    public sealed class ProcessSystemInfo
    {
        /// <summary>
        /// 处理器架构编码 (如 9: x64, 0: x86)
        /// </summary>
        public uint ProcessorArchitecture { get; set; }

        /// <summary>
        /// CPU 核心数
        /// </summary>
        public uint NumberOfProcessors { get; set; }

        /// <summary>
        /// 系统主版本号
        /// </summary>
        public uint MajorVersion { get; set; }

        /// <summary>
        /// 系统次版本号
        /// </summary>
        public uint MinorVersion { get; set; }

        /// <summary>
        /// 系统内部构建号
        /// </summary>
        public uint BuildNumber { get; set; }

        /// <summary>
        /// Service Pack 补丁版本描述
        /// </summary>
        public string CSDVersion { get; set; } = string.Empty;

        /// <summary>
        /// 架构文字描述
        /// </summary>
        public string ArchitectureDisplay => ProcessorArchitecture switch
        {
            9 => "x64 (AMD64)",
            0 => "x86",
            12 => "ARM64",
            _ => $"Arch_{ProcessorArchitecture}"
        };

        /// <summary>
        /// Windows 系统完整版本串
        /// </summary>
        public string OSVersionDisplay => $"Windows NT {MajorVersion}.{MinorVersion} (Build {BuildNumber}) {CSDVersion}".Trim();
    }
}
