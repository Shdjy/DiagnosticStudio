using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Reports
{
    /// <summary>
    /// 诊断报告脱敏配置选项
    /// </summary>
    public sealed class ReportSanitizerOptions
    {
        /// <summary>
        /// 是否脱敏 Windows 用户名 (如 C:\Users\ZhangSan -> C:\Users\<USER>)
        /// </summary>
        public bool SanitizeUserNames { get; set; } = true;

        /// <summary>
        /// 是否脱敏私有源码目录前缀
        /// </summary>
        public bool SanitizeSourcePaths { get; set; } = true;

        /// <summary>
        /// 是否脱敏内网 IP 地址
        /// </summary>
        public bool SanitizeIpAddresses { get; set; } = true;

        /// <summary>
        /// 替换占位符
        /// </summary>
        public string RedactedMask { get; set; } = "[REDACTED]";
    }

    /// <summary>
    /// 诊断报告脱敏处理器接口
    /// 防止在公开汇报或跨部门流转时泄露敏感开发路径、用户名与内网网络信息
    /// </summary>
    public interface IReportSanitizer
    {
        /// <summary>
        /// 对文本字符串进行脱敏处理
        /// </summary>
        string SanitizeText(string text, ReportSanitizerOptions? options = null);

        /// <summary>
        /// 对整份 Dump 诊断报告模型进行深拷贝脱敏
        /// </summary>
        DumpReportModel SanitizeReport(DumpReportModel original, ReportSanitizerOptions? options = null);
    }
}
