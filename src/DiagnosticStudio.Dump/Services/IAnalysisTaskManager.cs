using System;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Dump.Services
{
    /// <summary>
    /// 转储分析任务全生命周期与资源限制调度器接口
    /// </summary>
    public interface IAnalysisTaskManager
    {
        /// <summary>
        /// 调度并执行转储分析任务（具备排队、并发配额、进度通知与超时取消）
        /// </summary>
        Task<DumpReportModel> ExecuteAnalysisTaskAsync(
            string dumpFilePath,
            string? symbolPath = null,
            IProgress<AnalysisProgressInfo>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取当前配置的资源限制
        /// </summary>
        AnalysisResourceLimits Limits { get; }
    }
}
