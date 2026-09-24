using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 诊断规则接口
    /// </summary>
    public interface IDiagnosticRule
    {
        /// <summary>
        /// 规则唯一标识名称
        /// </summary>
        string RuleName { get; }

        /// <summary>
        /// 规则描述
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 对 Dump 报告执行诊断规则分析
        /// </summary>
        /// <param name="dumpReport">Dump 分析数据模型</param>
        /// <returns>发现的问题集合</returns>
        IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport);
    }
}
