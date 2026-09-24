using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DiagnosticStudio.Core.Logging;
using DiagnosticStudio.Core.Storage;

namespace DiagnosticStudio.Monitor.Services
{
    /// <summary>
    /// 运行中进程无损热快照抓取服务实现
    /// </summary>
    public sealed class LiveSnapshotService : ILiveSnapshotService
    {
        private readonly IDiagnosticLogger _logger;
        private readonly IWorkspaceStorageService _storageService;

        /// <summary>
        /// 构造运行中进程快照抓取服务
        /// </summary>
        /// <param name="logger">日志服务实例</param>
        /// <param name="storageService">工作区存储管理服务</param>
        public LiveSnapshotService(IDiagnosticLogger? logger = null, IWorkspaceStorageService? storageService = null)
        {
            _logger = logger ?? DiagnosticLogger.Instance;
            _storageService = storageService ?? WorkspaceStorageService.Instance;
        }

        [DllImport("DiagnosticNative.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int CaptureProcessLiveDump(uint processId, string dumpFilePath, int dumpType);

        /// <summary>
        /// 对指定的运行中进程捕获瞬态热快照转储 (Live MiniDump)
        /// 格式: DiagnosticsData/Snapshots/{ProcessName}_{PID}_{yyyyMMdd_HHmmss}.dmp
        /// </summary>
        /// <param name="processId">目标进程系统 PID</param>
        /// <param name="processName">目标进程名称（用于文件名标识）</param>
        /// <param name="outputDumpPath">目标保存文件或保存目录。若为 null，则自动使用工作区 Snapshots 目录并结构化命名</param>
        /// <param name="fullMemory">是否抓取完整进程内存 (Full Memory Dump)</param>
        /// <returns>实际生成的转储 Dump 文件完整物理路径</returns>
        public async Task<string> CaptureSnapshotAsync(int processId, string? processName = null, string? outputDumpPath = null, bool fullMemory = false)
        {
            string resolvedName = processName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(resolvedName))
            {
                try
                {
                    using var p = Process.GetProcessById(processId);
                    resolvedName = p.ProcessName;
                }
                catch
                {
                    resolvedName = "Process";
                }
            }

            string targetFile;
            if (string.IsNullOrWhiteSpace(outputDumpPath))
            {
                targetFile = _storageService.GenerateSnapshotFilePath(resolvedName, processId);
            }
            else
            {
                string fullPath = Path.GetFullPath(outputDumpPath);

                // 若传入的是已有目录，或没有文件扩展名（即作为保存目录对待）
                if (Directory.Exists(fullPath) || string.IsNullOrEmpty(Path.GetExtension(fullPath)))
                {
                    if (!Directory.Exists(fullPath))
                    {
                        Directory.CreateDirectory(fullPath);
                    }
                    string timeStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    targetFile = Path.Combine(fullPath, $"{resolvedName}_{processId}_{timeStr}.dmp");
                }
                else
                {
                    string? parentDir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }
                    targetFile = fullPath;
                }
            }

            return await Task.Run(() =>
            {
                using var measure = _logger.Measure("LiveSnapshot", $"抓取进程 PID {processId} ({resolvedName}) 快照至 {targetFile}");

                int dumpTypeFlag = fullMemory ? 1 : 0;
                int result = CaptureProcessLiveDump((uint)processId, targetFile, dumpTypeFlag);

                if (result != 0)
                {
                    string errMsg = $"抓取进程快照失败，Windows 错误码: 0x{result:X8}";
                    _logger.Error("LiveSnapshot", errMsg);
                    throw new InvalidOperationException(errMsg);
                }

                _logger.Info("LiveSnapshot", $"快照抓取成功，文件保存至: {targetFile}，大小: {new FileInfo(targetFile).Length / 1024} KB");
                return targetFile;
            });
        }
    }
}