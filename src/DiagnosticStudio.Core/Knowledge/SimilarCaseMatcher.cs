using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Knowledge
{
    /// <summary>
    /// 相似故障案例智能检索匹配器实现
    /// 综合异常码、模块名、责任函数及栈特征进行多维加权相似度比对
    /// </summary>
    public sealed class SimilarCaseMatcher : ISimilarCaseMatcher
    {
        private readonly IFaultCaseRepository _repository;

        public SimilarCaseMatcher(IFaultCaseRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public List<CaseMatchResult> FindSimilarCases(DumpReportModel dumpReport, double minScore = 0.3)
        {
            var results = new List<CaseMatchResult>();
            if (dumpReport == null)
            {
                return results;
            }

            var allCases = _repository.GetAllCases();
            if (allCases.Count == 0)
            {
                return results;
            }

            uint currentExpCode = dumpReport.Exception?.ExceptionCode ?? 0;
            var crashThread = dumpReport.Threads.FirstOrDefault(t => t.IsCrashThread) ?? dumpReport.Threads.FirstOrDefault();
            var topUserFrame = crashThread?.Frames.FirstOrDefault(f => f.IsUserCode) ?? crashThread?.Frames.FirstOrDefault();

            string currentModule = topUserFrame?.ModuleName ?? string.Empty;
            string currentFunc = topUserFrame?.FunctionName ?? string.Empty;

            foreach (var c in allCases)
            {
                double score = 0.0;
                var reasons = new List<string>();

                // 1. 异常码精准匹配 (权重 0.40)
                if (currentExpCode != 0 && c.ExceptionCode == currentExpCode)
                {
                    score += 0.40;
                    reasons.Add($"异常代码完全匹配 (0x{currentExpCode:X8})");
                }

                // 2. 责任模块模糊匹配 (权重 0.30)
                if (!string.IsNullOrEmpty(currentModule) && !string.IsNullOrEmpty(c.FaultingModule))
                {
                    string m1 = Path.GetFileNameWithoutExtension(currentModule).ToLowerInvariant();
                    string m2 = Path.GetFileNameWithoutExtension(c.FaultingModule).ToLowerInvariant();

                    if (m1 == m2 || m1.Contains(m2) || m2.Contains(m1))
                    {
                        score += 0.30;
                        reasons.Add($"责任模块名称高度吻合 ({c.FaultingModule})");
                    }
                }

                // 3. 责任函数或调用栈匹配 (权重 0.30)
                if (!string.IsNullOrEmpty(currentFunc) && !string.IsNullOrEmpty(c.FaultingFunction))
                {
                    string f1 = currentFunc.ToLowerInvariant();
                    string f2 = c.FaultingFunction.ToLowerInvariant();

                    if (f1 == f2 || f1.Contains(f2) || f2.Contains(f1))
                    {
                        score += 0.30;
                        reasons.Add($"责任函数名称匹配 ({c.FaultingFunction})");
                    }
                }
                else if (crashThread?.Frames != null && !string.IsNullOrEmpty(c.FaultingFunction))
                {
                    // 在调用栈其他帧中寻找匹配
                    bool inStack = crashThread.Frames.Any(f => f.FunctionName.Contains(c.FaultingFunction, StringComparison.OrdinalIgnoreCase));
                    if (inStack)
                    {
                        score += 0.20;
                        reasons.Add($"调用栈中包含关联责任函数 ({c.FaultingFunction})");
                    }
                }

                if (score >= minScore)
                {
                    results.Add(new CaseMatchResult
                    {
                        Case = c,
                        SimilarityScore = Math.Min(1.0, score),
                        MatchReasons = reasons
                    });
                }
            }

            return results.OrderByDescending(r => r.SimilarityScore).ToList();
        }
    }
}
