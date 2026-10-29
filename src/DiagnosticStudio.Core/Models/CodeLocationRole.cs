namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 代码栈帧在诊断过程中的归因角色分类
    /// 避免将“异常发生点/被动发现点”直接误定性为“根本原因”
    /// </summary>
    public enum CodeLocationRole
    {
        /// <summary>
        /// 未知或未分类
        /// </summary>
        Unspecified,

        /// <summary>
        /// 异常直接爆发的硬件指令位置（如第 0 帧崩溃指令，可能在系统底层库或业务代码）
        /// </summary>
        ExceptionInstruction,

        /// <summary>
        /// 业务代码中直接触发调用的栈帧（如调用了引发崩溃系统 API 的最外层业务函数）
        /// </summary>
        DirectTriggerCall,

        /// <summary>
        /// 可疑业务代码/破坏上下文代码（如前面可能破坏了堆状态或传入野指针的调用点）
        /// </summary>
        SuspectBusinessCode,

        /// <summary>
        /// 诊断推断出的候选根因位置（待进一步验证）
        /// </summary>
        RootCauseCandidate,

        /// <summary>
        /// 经多项确凿证据链确认的最终根因位置
        /// </summary>
        VerifiedRootCause
    }

    /// <summary>
    /// 根因确定性状态
    /// </summary>
    public enum RootCauseCertainty
    {
        /// <summary>
        /// 证据不足，仅列出候选位置或可能原因
        /// </summary>
        InsufficientEvidence,

        /// <summary>
        /// 依据调用栈与异常特征推断的合理诊断假设
        /// </summary>
        ProbableHypothesis,

        /// <summary>
        /// 具备多项独立证据支撑且经过校验的确凿根因
        /// </summary>
        ConfirmedRootCause
    }
}
