using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 诊断规则接口
    /// 支持规则元数据、版本管理、优先级与动态启停
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
        /// 规则标识符
        /// </summary>
        string RuleId => RuleName;

        /// <summary>
        /// 规则版本号
        /// </summary>
        string Version => "1.0.0";

        /// <summary>
        /// 规则是否处于启用状态
        /// </summary>
        bool IsEnabled => true;

        /// <summary>
        /// 规则执行优先级（数值越小越先执行）
        /// </summary>
        int Priority => 100;

        /// <summary>
        /// 对 Dump 报告执行诊断规则分析
        /// </summary>
        /// <param name="dumpReport">Dump 分析数据模型</param>
        /// <returns>发现的问题集合</returns>
        IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport);
    }
}
