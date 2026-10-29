using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DiagnosticStudio.Core.Logging;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 默认智能诊断规则引擎实现
    /// 具备规则异常完全可见审计、沙箱故障隔离、执行耗时度量与置信度校准能力
    /// </summary>
    public sealed class DefaultRuleEngine : IRuleEngine
    {
        private const string ModuleTag = "RuleEngine";
        private readonly List<IDiagnosticRule> _rules = new List<IDiagnosticRule>();
        private readonly IDiagnosticLogger _logger;

        public IReadOnlyList<IDiagnosticRule> Rules => _rules.AsReadOnly();

        public DefaultRuleEngine()
            : this(DiagnosticLogger.Instance)
        {
        }

        public DefaultRuleEngine(IDiagnosticLogger logger)
        {
            _logger = logger ?? DiagnosticLogger.Instance;

            // 注册内置核心诊断规则 (先专有规则，后场景与兜底规则)
            RegisterRule(new NullPointerRule());
            RegisterRule(new AccessViolationRule());
            RegisterRule(new BufferOverflowRule());
            RegisterRule(new UnhandledCppExceptionRule());
            RegisterRule(new DivideByZeroRule());
            RegisterRule(new StackOverflowRule());
            RegisterRule(new HeapCorruptionRule());
            RegisterRule(new AssertOrBreakpointRule());
            RegisterRule(new HangOrDeadlockRule());
            RegisterRule(new LiveSnapshotMemoryLeakRule());
            RegisterRule(new CorruptedDumpRecoveryRule());
            RegisterRule(new GeneralCrashFallbackRule());
        }

        public void RegisterRule(IDiagnosticRule rule)
        {
            if (rule != null && !_rules.Any(r => r.RuleName == rule.RuleName))
            {
                _rules.Add(rule);
            }
        }

        public List<DiagnosticFinding> Analyze(DumpReportModel dumpReport)
        {
            var results = new List<DiagnosticFinding>();
            if (dumpReport == null)
            {
                return results;
            }

            dumpReport.RuleExecutions.Clear();
            var sortedRules = _rules.OrderBy(r => r.Priority).ToList();
            int failedCount = 0;
            int executedCount = 0;

            foreach (var rule in sortedRules)
            {
                var record = new RuleExecutionRecord
                {
                    RuleId = rule.RuleId,
                    RuleName = rule.RuleName,
                    Version = rule.Version
                };

                if (!rule.IsEnabled)
                {
                    record.Status = RuleExecutionStatus.Skipped;
                    record.SkipReason = "规则已被配置禁用";
                    dumpReport.RuleExecutions.Add(record);
                    continue;
                }

                executedCount++;
                var sw = Stopwatch.StartNew();
                try
                {
                    var findings = rule.Evaluate(dumpReport);
                    sw.Stop();
                    record.ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds;
                    record.Status = RuleExecutionStatus.Success;

                    if (findings != null)
                    {
                        var findingsList = findings.ToList();
                        record.FindingsCount = findingsList.Count;

                        foreach (var f in findingsList)
                        {
                            if (string.IsNullOrEmpty(f.RuleId))
                            {
                                f.RuleId = rule.RuleId;
                            }
                            if (string.IsNullOrEmpty(f.RuleVersion))
                            {
                                f.RuleVersion = rule.Version;
                            }
                            results.Add(f);
                        }
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    failedCount++;
                    record.ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds;
                    record.Status = RuleExecutionStatus.Failed;
                    record.ExceptionType = ex.GetType().FullName;
                    record.ErrorMessage = ex.Message;
                    record.StackTrace = ex.StackTrace;

                    _logger.Error(ModuleTag, $"规则 [{rule.RuleName}] 执行异常: {ex.Message}", ex);
                }

                dumpReport.RuleExecutions.Add(record);
            }

            // 计算整体分析状态 (成功 / 部分完成 / 失败)
            if (failedCount == 0)
            {
                dumpReport.OverallStatus = AnalysisOverallStatus.Success;
            }
            else if (failedCount < executedCount)
            {
                dumpReport.OverallStatus = AnalysisOverallStatus.PartialSuccess;
            }
            else
            {
                dumpReport.OverallStatus = AnalysisOverallStatus.Failed;
            }

            // 按严重等级由高到低排序 (Critical -> High -> Warning -> Info)
            return results.OrderByDescending(f => f.Level).ThenByDescending(f => f.Confidence).ToList();
        }
    }
}
