using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Knowledge
{
    /// <summary>
    /// 案例匹配结果条目
    /// </summary>
    public sealed class CaseMatchResult
    {
        public FaultCaseItem Case { get; set; } = new FaultCaseItem();
        public double SimilarityScore { get; set; }
        public List<string> MatchReasons { get; set; } = new List<string>();
    }

    /// <summary>
    /// 相似故障案例智能检索匹配器接口
    /// </summary>
    public interface ISimilarCaseMatcher
    {
        /// <summary>
        /// 根据当前 Dump 报告特征在案例库中检索相似历史案例
        /// </summary>
        List<CaseMatchResult> FindSimilarCases(DumpReportModel dumpReport, double minScore = 0.3);
    }
}
