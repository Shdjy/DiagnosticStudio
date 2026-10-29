using System;

namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 证据分类类别
    /// </summary>
    public enum EvidenceCategory
    {
        /// <summary>
        /// 已确认为真实的底层事实（如异常码、寄存器指针、模块信息）
        /// </summary>
        ConfirmedFact,

        /// <summary>
        /// 辅助推断证据（如符号匹配、栈帧上下文、内存属性）
        /// </summary>
        SupportingEvidence,

        /// <summary>
        /// 尚未完全证实的诊断假设（如根据地址推断为空指针解引用）
        /// </summary>
        Hypothesis,

        /// <summary>
        /// 建议进行的复现或排查验证步骤
        /// </summary>
        VerificationStep,

        /// <summary>
        /// 反向或排除证据（用于排除其他可能原因的依据）
        /// </summary>
        CounterEvidence
    }

    /// <summary>
    /// 统一结构化证据项模型
    /// </summary>
    public sealed class EvidenceItem
    {
        /// <summary>
        /// 证据分类类别
        /// </summary>
        public EvidenceCategory Category { get; set; } = EvidenceCategory.SupportingEvidence;

        /// <summary>
        /// 证据内容详细描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 证据可信度权重贡献 (0.0 ~ 1.0)
        /// </summary>
        public double Weight { get; set; } = 1.0;

        /// <summary>
        /// 关联内存虚拟地址（可选）
        /// </summary>
        public ulong? RelatedAddress { get; set; }

        /// <summary>
        /// 关联的模块名称（可选）
        /// </summary>
        public string? RelatedModule { get; set; }

        /// <summary>
        /// 关联的线程 ID（可选）
        /// </summary>
        public uint? RelatedThreadId { get; set; }

        /// <summary>
        /// 关联寄存器名称（可选，如 RIP, RSP, RAX）
        /// </summary>
        public string? RelatedRegister { get; set; }

        /// <summary>
        /// 关联源代码位置描述（可选）
        /// </summary>
        public string? SourceLocation { get; set; }

        public EvidenceItem()
        {
        }

        public EvidenceItem(EvidenceCategory category, string description, double weight = 1.0)
        {
            Category = category;
            Description = description;
            Weight = weight;
        }

        public override string ToString()
        {
            return $"[{Category}] {Description}";
        }
    }
}
