using System.Threading;
using System.Threading.Tasks;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Dump.Services
{
    /// <summary>
    /// Dump 转储文件分析服务接口
    /// </summary>
    public interface IDumpAnalyzer
    {
        /// <summary>
        /// 异步解析指定的 Dump 转储文件并输出完整诊断报告
        /// </summary>
        /// <param name="dumpFilePath">转储文件物理路径</param>
        /// <param name="symbolPath">符号搜索路径（若为空则自动采用默认符号缓存与微软符号服务器）</param>
        /// <param name="cancellationToken">取消标记</param>
        /// <returns>综合诊断报告数据模型</returns>
        Task<DumpReportModel> AnalyzeAsync(string dumpFilePath, string? symbolPath = null, CancellationToken cancellationToken = default);
    }
}
