using System.Threading.Tasks;

namespace DiagnosticStudio.Monitor.Services
{
    /// <summary>
    /// 运行中进程无损热快照抓取服务接口
    /// </summary>
    public interface ILiveSnapshotService
    {
        /// <summary>
        /// 对指定的运行中进程抓取快照并保存为 Dump 文件
        /// </summary>
        /// <param name="processId">目标进程 PID</param>
        /// <param name="processName">目标进程名称（用于文件名区分标识，如 CppCrashTarget）</param>
        /// <param name="outputDumpPath">输出文件路径或输出目录（若为 null 则自动保存至工作区 Snapshots 目录）</param>
        /// <param name="fullMemory">是否抓取全内存转储（true: 全内存, false: 典型轻量转储）</param>
        /// <returns>生成的 Dump 文件完整绝对路径</returns>
        Task<string> CaptureSnapshotAsync(int processId, string? processName = null, string? outputDumpPath = null, bool fullMemory = false);
    }
}