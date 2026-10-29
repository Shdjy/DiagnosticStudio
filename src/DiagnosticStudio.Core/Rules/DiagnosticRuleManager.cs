using System;
using System.Collections.Generic;
using System.Linq;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 可配置诊断规则包装器
    /// </summary>
    public sealed class ConfigurableDiagnosticRuleWrapper : IDiagnosticRule
    {
        private readonly IDiagnosticRule _inner;

        public string RuleName => _inner.RuleName;
        public string Description => _inner.Description;
        public string RuleId => _inner.RuleId;
        public string Version => _inner.Version;

        public bool IsEnabled { get; set; } = true;
        public int Priority { get; set; } = 100;

        public ConfigurableDiagnosticRuleWrapper(IDiagnosticRule inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            IsEnabled = inner.IsEnabled;
            Priority = inner.Priority;
        }

        public IEnumerable<DiagnosticStudio.Core.Models.DiagnosticFinding> Evaluate(DiagnosticStudio.Core.Models.DumpReportModel dumpReport)
        {
            if (!IsEnabled)
            {
                return Enumerable.Empty<DiagnosticStudio.Core.Models.DiagnosticFinding>();
            }

            return _inner.Evaluate(dumpReport);
        }
    }

    /// <summary>
    /// 诊断规则动态管理注册中心实现
    /// </summary>
    public sealed class DiagnosticRuleManager : IRuleRegistry
    {
        private readonly Dictionary<string, ConfigurableDiagnosticRuleWrapper> _rules = new Dictionary<string, ConfigurableDiagnosticRuleWrapper>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        public DiagnosticRuleManager()
        {
        }

        public IReadOnlyList<IDiagnosticRule> GetAllRules()
        {
            lock (_lock)
            {
                return _rules.Values.OrderBy(r => r.Priority).ToList();
            }
        }

        public void RegisterRule(IDiagnosticRule rule)
        {
            if (rule == null)
            {
                return;
            }

            lock (_lock)
            {
                _rules[rule.RuleId] = new ConfigurableDiagnosticRuleWrapper(rule);
            }
        }

        public bool UnregisterRule(string ruleId)
        {
            if (string.IsNullOrEmpty(ruleId))
            {
                return false;
            }

            lock (_lock)
            {
                return _rules.Remove(ruleId);
            }
        }

        public bool SetRuleEnabled(string ruleId, bool isEnabled)
        {
            if (string.IsNullOrEmpty(ruleId))
            {
                return false;
            }

            lock (_lock)
            {
                if (_rules.TryGetValue(ruleId, out var wrapper))
                {
                    wrapper.IsEnabled = isEnabled;
                    return true;
                }
                return false;
            }
        }

        public bool SetRulePriority(string ruleId, int priority)
        {
            if (string.IsNullOrEmpty(ruleId))
            {
                return false;
            }

            lock (_lock)
            {
                if (_rules.TryGetValue(ruleId, out var wrapper))
                {
                    wrapper.Priority = priority;
                    return true;
                }
                return false;
            }
        }
    }
}
