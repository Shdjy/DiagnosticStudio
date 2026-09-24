using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 诊断规则引擎接口
    /// </summary>
    public interface IRuleEngine
    {
        /// <summary>
        /// 注册自定义规则
        /// </summary>
        /// <param name="rule">诊断规则实例</param>
        void RegisterRule(IDiagnosticRule rule);

        /// <summary>
        /// 获取所有已加载的规则
        /// </summary>
        IReadOnlyList<IDiagnosticRule> Rules { get; }

        /// <summary>
        /// 执行全部规则并输出综合诊断项
        /// </summary>
        /// <param name="dumpReport">Dump 分析报告</param>
        /// <returns>诊断问题项集合</returns>
        List<DiagnosticFinding> Analyze(DumpReportModel dumpReport);
    }
}
