using System;
using System.Collections.Generic;

namespace DiagnosticStudio.Core.Knowledge
{
    /// <summary>
    /// 历史故障案例结构化实体模型
    /// 用于团队故障经验沉淀、根因归档与相似故障智能推荐
    /// </summary>
    public sealed class FaultCaseItem
    {
        /// <summary>
        /// 案例编号 (如 CASE-2026-001)
        /// </summary>
        public string CaseId { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        /// <summary>
        /// 案例标题简述
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 异常代码 (如 0xC0000005)
        /// </summary>
        public uint ExceptionCode { get; set; }

        /// <summary>
        /// 责任模块名 (如 AlgorithmPlugin.dll)
        /// </summary>
        public string FaultingModule { get; set; } = string.Empty;

        /// <summary>
        /// 责任函数名 (如 ProcessFrame)
        /// </summary>
        public string FaultingFunction { get; set; } = string.Empty;

        /// <summary>
        /// 关键调用栈签名特征哈希
        /// </summary>
        public string StackSignatureHash { get; set; } = string.Empty;

        /// <summary>
        /// 根本原因分类 (如 Use-After-Free, Deadlock, GDI-Leak, NullPointer, StackOverflow)
        /// </summary>
        public string RootCauseCategory { get; set; } = string.Empty;

        /// <summary>
        /// 最终排查确立的根本原因详细剖析
        /// </summary>
        public string RootCauseDetail { get; set; } = string.Empty;

        /// <summary>
        /// 已验证的修复解决方案与代码示例
        /// </summary>
        public string SolutionDescription { get; set; } = string.Empty;

        /// <summary>
        /// 验证复核记录与测试方法
        /// </summary>
        public string VerificationNotes { get; set; } = string.Empty;

        /// <summary>
        /// 标签列表 (如 "相机", "内存", "多线程")
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// 归档时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
