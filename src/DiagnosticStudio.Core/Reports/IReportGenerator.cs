using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Reports
{
    /// <summary>
    /// 诊断报告生成器接口
    /// </summary>
    public interface IReportGenerator
    {
        /// <summary>
        /// 生成报告内容字符串
        /// </summary>
        /// <param name="reportModel">Dump 诊断报告数据</param>
        /// <returns>格式化的报告文本内容</returns>
        string Generate(DumpReportModel reportModel);

        /// <summary>
        /// 导出报告到目标文件
        /// </summary>
        /// <param name="reportModel">Dump 诊断报告数据</param>
        /// <param name="targetFilePath">目标保存文件绝对路径</param>
        void ExportToFile(DumpReportModel reportModel, string targetFilePath);
    }
}
