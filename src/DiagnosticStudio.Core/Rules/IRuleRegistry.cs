using System.Collections.Generic;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 诊断规则注册与动态生命周期管理器接口
    /// </summary>
    public interface IRuleRegistry
    {
        IReadOnlyList<IDiagnosticRule> GetAllRules();
        void RegisterRule(IDiagnosticRule rule);
        bool UnregisterRule(string ruleId);
        bool SetRuleEnabled(string ruleId, bool isEnabled);
        bool SetRulePriority(string ruleId, int priority);
    }
}
