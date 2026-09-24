using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Dump.Services
{
    /// <summary>
    /// 可疑代码行识别与分级标记定位器接口
    /// </summary>
    public interface ISuspectCodeLocator
    {
        /// <summary>
        /// 判定指定模块名称是否属于用户自身编写的业务代码模块
        /// </summary>
        /// <param name="moduleName">模块名 (如 MyApp.dll)</param>
        /// <returns>true 表示用户业务模块，false 表示系统或三方底层库</returns>
        bool IsUserModule(string moduleName);

        /// <summary>
        /// 对整个 Dump 中的所有线程调用栈进行智能标记与风险分级赋色
        /// </summary>
        /// <param name="threads">线程列表</param>
        /// <param name="crashThreadId">崩溃线程 ID</param>
        void MarkSuspectFrames(List<ThreadInfo> threads, uint? crashThreadId);

        /// <summary>
        /// 从崩溃线程调用栈中查找引发崩溃的最底层用户业务责任代码帧 (Blamed User Frame)
        /// </summary>
        /// <param name="crashThread">崩溃线程信息</param>
        /// <returns>用户责任代码帧，若无则返回 null</returns>
        StackFrameInfo? FindBlamedUserFrame(ThreadInfo? crashThread);
    }
}
