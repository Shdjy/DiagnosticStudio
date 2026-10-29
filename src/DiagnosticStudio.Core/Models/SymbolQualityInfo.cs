namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 调试符号质量状态
    /// </summary>
    public enum SymbolQualityStatus
    {
        /// <summary>
        /// 完整匹配的专属调试符号 (Matched PDB)
        /// </summary>
        MatchedPdb,

        /// <summary>
        /// 仅包含导出表函数名，缺少源码行号与局部变量 (Export Table Only)
        /// </summary>
        ExportSymbolsOnly,

        /// <summary>
        /// 符号与模块 GUID / Age 签名不匹配 (Mismatched PDB)
        /// </summary>
        MismatchedPdb,

        /// <summary>
        /// 未找到任何符号文件 (No Symbols)
        /// </summary>
        NoSymbols,

        /// <summary>
        /// 符号加载失败或格式损坏 (Load Failed)
        /// </summary>
        LoadFailed
    }

    /// <summary>
    /// 模块级符号加载质量模型
    /// </summary>
    public sealed class SymbolQualityInfo
    {
        /// <summary>
        /// 符号质量等级
        /// </summary>
        public SymbolQualityStatus Status { get; set; } = SymbolQualityStatus.NoSymbols;

        /// <summary>
        /// 实际加载的 PDB 物理路径
        /// </summary>
        public string LoadedPdbPath { get; set; } = string.Empty;

        /// <summary>
        /// PDB 签名 GUID 字符串
        /// </summary>
        public string PdbGuid { get; set; } = string.Empty;

        /// <summary>
        /// PDB 年龄计数 (Age)
        /// </summary>
        public uint PdbAge { get; set; }

        /// <summary>
        /// 是否可用源码行号精确定位
        /// </summary>
        public bool HasSourceLineInfo { get; set; }

        /// <summary>
        /// 质量描述摘要
        /// </summary>
        public string QualitySummary => Status switch
        {
            SymbolQualityStatus.MatchedPdb => HasSourceLineInfo ? "精确源码符号 (PDB + Line)" : "匹配符号 (仅函数名)",
            SymbolQualityStatus.ExportSymbolsOnly => "导出表基础符号 (无源码映射)",
            SymbolQualityStatus.MismatchedPdb => "⚠️ 符号版本不匹配 (结果可能失真)",
            SymbolQualityStatus.LoadFailed => "❌ 符号加载失败",
            _ => "无符号"
        };
    }
}
