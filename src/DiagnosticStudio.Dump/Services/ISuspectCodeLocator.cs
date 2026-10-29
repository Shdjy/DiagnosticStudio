using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Dump.Services
{
    /// <summary>
    /// 可疑代码智能定位与风险标记器接口
    /// 遵循归因角色分层原则，区分异常爆发点、业务触发点与根因候选点
    /// </summary>
    public interface ISuspectCodeLocator
    {
        /// <summary>
        /// 判定指定模块是否属于用户业务代码（排除系统底层库与常用第三方运行时）
        /// </summary>
        bool IsUserModule(string moduleName);

        /// <summary>
        /// 对线程集合进行智能风险标记与层级高亮染色
        /// </summary>
        void MarkSuspectFrames(List<ThreadInfo> threads, uint? crashThreadId);

        /// <summary>
        /// 在崩溃调用栈中寻找最外层的用户责任代码栈帧（向后兼容）
        /// </summary>
        StackFrameInfo? FindBlamedUserFrame(ThreadInfo? crashThread);

        /// <summary>
        /// 深度构建故障调用链条（异常指令点 -> 业务直接触发点 -> 可疑破坏点 -> 根因候选）
        /// </summary>
        CodeLocationChain BuildLocationChain(ThreadInfo? crashThread, ExceptionInfo? exception);
    }
}
