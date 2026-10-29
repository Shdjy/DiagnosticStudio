using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 默认智能诊断规则引擎实现
    /// </summary>
    public sealed class DefaultRuleEngine : IRuleEngine
    {
        private readonly List<IDiagnosticRule> _rules = new List<IDiagnosticRule>();

        public IReadOnlyList<IDiagnosticRule> Rules => _rules.AsReadOnly();

        public DefaultRuleEngine()
        {
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

            foreach (var rule in _rules)
            {
                try
                {
                    var findings = rule.Evaluate(dumpReport);
                    if (findings != null)
                    {
                        results.AddRange(findings);
                    }
                }
                catch
                {
                    // 规则执行沙箱隔离，单个规则异常不阻断全局分析
                }
            }

            // 按严重等级由高到低排序 (Critical -> High -> Warning -> Info)
            return results.OrderByDescending(f => f.Level).ThenByDescending(f => f.Confidence).ToList();
        }
    }
}
