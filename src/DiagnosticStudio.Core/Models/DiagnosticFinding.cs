using System;
using System.Collections.Generic;

namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 诊断结论统一数据模型
    /// 包含结构化证据链条、可信度分级、定位角色划分与验证步骤
    /// </summary>
    public sealed class DiagnosticFinding
    {
        /// <summary>
        /// 诊断结论唯一编号 (如 FINDING-AV-001)
        /// </summary>
        public string FindingId { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        /// <summary>
        /// 产出此结论的规则标识
        /// </summary>
        public string RuleId { get; set; } = string.Empty;

        /// <summary>
        /// 规则版本号
        /// </summary>
        public string RuleVersion { get; set; } = "1.0.0";

        /// <summary>
        /// 严重度级别 (Critical 🔴 / High 🟠 / Warning 🟡 / Info 🔵)
        /// </summary>
        public DiagnosticLevel Level { get; set; } = DiagnosticLevel.Info;

        /// <summary>
        /// 诊断标题 (如 Potential Null Pointer Dereference)
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 详细现象与原因剖析
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 触发异常的可疑模块
        /// </summary>
        public string Module { get; set; } = string.Empty;

        /// <summary>
        /// 触发异常的可疑函数
        /// </summary>
        public string Function { get; set; } = string.Empty;

        /// <summary>
        /// 关联源代码绝对路径
        /// </summary>
        public string SourceFile { get; set; } = string.Empty;

        /// <summary>
        /// 关联源码行号
        /// </summary>
        public uint Line { get; set; }

        /// <summary>
        /// 规则置信度 (0.0 ~ 1.0)，由多维证据质量动态决定
        /// </summary>
        public double Confidence { get; set; } = 1.0;

        /// <summary>
        /// 代码定位角色分类 (区分异常指令爆发点 vs 直接触发调用点 vs 破坏根因)
        /// </summary>
        public CodeLocationRole LocationRole { get; set; } = CodeLocationRole.Unspecified;

        /// <summary>
        /// 故障代码归因链条完整上下文
        /// </summary>
        public CodeLocationChain? LocationChain { get; set; }

        /// <summary>
        /// 原始诊断证据列表 (向后兼容)
        /// </summary>
        public List<string> Evidence { get; set; } = new List<string>();

        /// <summary>
        /// 结构化强类型证据链条集合
        /// </summary>
        public List<EvidenceItem> StructuredEvidences { get; set; } = new List<EvidenceItem>();

        /// <summary>
        /// 已确认为真实的底层事实列表 (Confirmed Facts)
        /// </summary>
        public List<string> ConfirmedFacts { get; set; } = new List<string>();

        /// <summary>
        /// 尚未完全证实的诊断假设与推测 (Hypotheses)
        /// </summary>
        public List<string> Hypotheses { get; set; } = new List<string>();

        /// <summary>
        /// 建议进行的验证与复查步骤 (Verification Steps)
        /// </summary>
        public List<string> VerificationSteps { get; set; } = new List<string>();

        /// <summary>
        /// 反向或排除证据列表 (Counter Evidences)
        /// </summary>
        public List<string> CounterEvidences { get; set; } = new List<string>();

        /// <summary>
        /// 现场排查与修复建议
        /// </summary>
        public List<string> Recommendations { get; set; } = new List<string>();

        /// <summary>
        /// 关联的问题调用栈
        /// </summary>
        public List<StackFrameInfo> RelatedStack { get; set; } = new List<StackFrameInfo>();

        /// <summary>
        /// 格式化级别 Emoji 标记
        /// </summary>
        public string LevelBadge => Level switch
        {
            DiagnosticLevel.Critical => "🔴 Critical",
            DiagnosticLevel.High => "🟠 High",
            DiagnosticLevel.Warning => "🟡 Warning",
            _ => "🔵 Info"
        };

        /// <summary>
        /// 格式化源码定位显示文本 (若无源码则显示函数，具备安全空 setter 兼容 WPF 绑定)
        /// </summary>
        public string CodeLocationDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(SourceFile))
                {
                    return string.IsNullOrEmpty(Function) ? "未关联到具体源码文件" : $"{Module}!{Function}";
                }
                return Line > 0 ? $"{SourceFile} (第 {Line} 行)" : SourceFile;
            }
            set
            {
            }
        }

        /// <summary>
        /// 便捷添加结构化证据并同步至文本证据列表中
        /// </summary>
        public void AddStructuredEvidence(
            EvidenceCategory category,
            string description,
            double weight = 1.0,
            ulong? address = null,
            string? module = null,
            uint? threadId = null)
        {
            var item = new EvidenceItem
            {
                Category = category,
                Description = description,
                Weight = weight,
                RelatedAddress = address,
                RelatedModule = module,
                RelatedThreadId = threadId
            };
            StructuredEvidences.Add(item);

            // 同步至旧列表保持向下兼容
            Evidence.Add(description);

            // 分类同步
            switch (category)
            {
                case EvidenceCategory.ConfirmedFact:
                    ConfirmedFacts.Add(description);
                    break;
                case EvidenceCategory.Hypothesis:
                    Hypotheses.Add(description);
                    break;
                case EvidenceCategory.VerificationStep:
                    VerificationSteps.Add(description);
                    break;
                case EvidenceCategory.CounterEvidence:
                    CounterEvidences.Add(description);
                    break;
            }
        }
    }
}
