using System.Collections.Generic;

namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 故障代码完整归因链条
    /// 完整还原：异常指令位置 -> 直接触发调用 -> 可疑破坏点 -> 根因候选与验证状态
    /// </summary>
    public sealed class CodeLocationChain
    {
        /// <summary>
        /// 异常直接爆发点栈帧 (Exception Instruction)
        /// </summary>
        public StackFrameInfo? ExceptionPoint { get; set; }

        /// <summary>
        /// 业务代码直接触发点栈帧 (Direct Trigger Frame)
        /// </summary>
        public StackFrameInfo? DirectTriggerPoint { get; set; }

        /// <summary>
        /// 可疑业务逻辑/破坏状态上下文栈帧 (Suspect Business Point)
        /// </summary>
        public StackFrameInfo? SuspectBusinessPoint { get; set; }

        /// <summary>
        /// 推导出的候选根因栈帧 (Candidate Root Cause)
        /// </summary>
        public StackFrameInfo? RootCauseCandidatePoint { get; set; }

        /// <summary>
        /// 根因结论确信度分级
        /// </summary>
        public RootCauseCertainty Certainty { get; set; } = RootCauseCertainty.ProbableHypothesis;

        /// <summary>
        /// 尚未完全确认的推测或疑点说明
        /// </summary>
        public List<string> UnconfirmedAssumptions { get; set; } = new List<string>();

        /// <summary>
        /// 建议的现场复核与验证步骤
        /// </summary>
        public List<string> VerificationSteps { get; set; } = new List<string>();

        /// <summary>
        /// 获取综合推荐呈现的首要导航位置（根据确定性智能选取）
        /// </summary>
        public StackFrameInfo? PrimaryNavigationPoint =>
            RootCauseCandidatePoint ?? DirectTriggerPoint ?? ExceptionPoint;
    }
}
