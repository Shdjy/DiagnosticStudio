using System;
using System.Diagnostics;
using System.IO;

namespace DiagnosticStudio.Core.Storage
{
    /// <summary>
    /// 诊断工作区数据存储与生命周期管理服务实现
    /// </summary>
    public sealed class WorkspaceStorageService : IWorkspaceStorageService
    {
        private static readonly Lazy<WorkspaceStorageService> _instance =
            new Lazy<WorkspaceStorageService>(() => new WorkspaceStorageService());

        /// <summary>
        /// 全局默认单例实例
        /// </summary>
        public static WorkspaceStorageService Instance => _instance.Value;

        /// <summary>
        /// 诊断数据工作区根目录完整路径
        /// </summary>
        public string RootDataDirectory { get; }

        /// <summary>
        /// 初始化工作区存储服务并确保基础目录树就绪
        /// </summary>
        /// <param name="customRootDirectory">自定义存储根目录，为空则使用应用程序根目录下的 DiagnosticsData 文件夹</param>
        public WorkspaceStorageService(string? customRootDirectory = null)
        {
            RootDataDirectory = string.IsNullOrWhiteSpace(customRootDirectory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DiagnosticsData")
                : Path.GetFullPath(customRootDirectory);

            EnsureDirectoriesExist();
        }

        private void EnsureDirectoriesExist()
        {
            try
            {
                if (!Directory.Exists(RootDataDirectory))
                {
                    Directory.CreateDirectory(RootDataDirectory);
                }

                string snapDir = Path.Combine(RootDataDirectory, "Snapshots");
                if (!Directory.Exists(snapDir))
                {
                    Directory.CreateDirectory(snapDir);
                }

                string logDir = Path.Combine(RootDataDirectory, "Logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                string rptDir = Path.Combine(RootDataDirectory, "Reports");
                if (!Directory.Exists(rptDir))
                {
                    Directory.CreateDirectory(rptDir);
                }
            }
            catch
            {
                // 静默容错
            }
        }

        public string GetSnapshotsDirectory()
        {
            string dir = Path.Combine(RootDataDirectory, "Snapshots");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        public string GetLogsDirectory()
        {
            string dir = Path.Combine(RootDataDirectory, "Logs");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        public string GetReportsDirectory()
        {
            string dir = Path.Combine(RootDataDirectory, "Reports");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        public string GetCurrentLogFilePath()
        {
            string logDir = GetLogsDirectory();
            string dateStr = DateTime.Now.ToString("yyyyMMdd");
            return Path.Combine(logDir, $"DiagnosticStudio_{dateStr}.log");
        }

        public string GenerateSnapshotFilePath(string? processName, int processId)
        {
            string safeName = SanitizeFileName(processName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "Process";
            }

            string snapDir = GetSnapshotsDirectory();
            string timeStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{safeName}_{processId}_{timeStr}.dmp";

            return Path.Combine(snapDir, fileName);
        }

        public StorageCleanupSummary GetCleanupSummary()
        {
            var summary = new StorageCleanupSummary();

            try
            {
                // 1. 统计 Snapshots 目录
                string snapDir = GetSnapshotsDirectory();
                if (Directory.Exists(snapDir))
                {
                    var files = Directory.GetFiles(snapDir, "*.*", SearchOption.AllDirectories);
                    summary.SnapshotFileCount += files.Length;
                    foreach (var file in files)
                    {
                        summary.SnapshotTotalBytes += SafeGetFileLength(file);
                    }
                }

                // 检查历史上误放在 exe 根目录下的 Snapshots 遗留文件或目录
                string legacySnap = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Snapshots");
                if (File.Exists(legacySnap))
                {
                    summary.SnapshotFileCount++;
                    summary.SnapshotTotalBytes += SafeGetFileLength(legacySnap);
                }

                // 2. 统计 Logs 目录
                string logDir = GetLogsDirectory();
                if (Directory.Exists(logDir))
                {
                    var files = Directory.GetFiles(logDir, "*.log", SearchOption.AllDirectories);
                    summary.LogFileCount += files.Length;
                    foreach (var file in files)
                    {
                        summary.LogTotalBytes += SafeGetFileLength(file);
                    }
                }

                // 检查历史上误放在 exe 根目录下的旧日志文件
                string legacyLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diagnostic_studio.log");
                if (File.Exists(legacyLog))
                {
                    summary.LogFileCount++;
                    summary.LogTotalBytes += SafeGetFileLength(legacyLog);
                }

                // 3. 统计 Reports 目录
                string rptDir = GetReportsDirectory();
                if (Directory.Exists(rptDir))
                {
                    var files = Directory.GetFiles(rptDir, "*.*", SearchOption.AllDirectories);
                    summary.ReportFileCount += files.Length;
                    foreach (var file in files)
                    {
                        summary.ReportTotalBytes += SafeGetFileLength(file);
                    }
                }
            }
            catch
            {
                // 忽略异常
            }

            return summary;
        }

        public StorageCleanupResult CleanIntermediateFiles(bool cleanSnapshots = true, bool cleanLogs = false, bool cleanReports = true)
        {
            var result = new StorageCleanupResult();

            // 1. 清理快照
            if (cleanSnapshots)
            {
                string snapDir = GetSnapshotsDirectory();
                CleanDirectoryFiles(snapDir, "*.*", result);

                // 清理旧遗留 Snapshots 文件
                string legacySnap = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Snapshots");
                if (File.Exists(legacySnap))
                {
                    SafeDeleteFile(legacySnap, result);
                }
            }

            // 2. 清理日志 (默认排除当天活动日志)
            if (cleanLogs)
            {
                string logDir = GetLogsDirectory();
                string currentLog = GetCurrentLogFilePath();

                if (Directory.Exists(logDir))
                {
                    foreach (var file in Directory.GetFiles(logDir, "*.log"))
                    {
                        if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(currentLog), StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // 保护当前进程日志
                        }
                        SafeDeleteFile(file, result);
                    }
                }

                // 清理旧日志文件
                string legacyLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diagnostic_studio.log");
                if (File.Exists(legacyLog))
                {
                    SafeDeleteFile(legacyLog, result);
                }
            }

            // 3. 清理临时报告
            if (cleanReports)
            {
                string rptDir = GetReportsDirectory();
                CleanDirectoryFiles(rptDir, "*.*", result);
            }

            result.Message = $"清理完成：成功删除 {result.DeletedFileCount} 个文件，释放磁盘空间 {result.FreedSizeDisplay}。" +
                             (result.FailedFileCount > 0 ? $"（另有 {result.FailedFileCount} 个文件因正被占用跳过）" : string.Empty);

            return result;
        }

        public void OpenDirectoryInExplorer(string? subDirectoryPath = null)
        {
            string target = string.IsNullOrWhiteSpace(subDirectoryPath) ? RootDataDirectory : subDirectoryPath;
            if (!Directory.Exists(target))
            {
                Directory.CreateDirectory(target);
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }
            catch
            {
                // 忽略打开异常
            }
        }

        private void CleanDirectoryFiles(string dir, string searchPattern, StorageCleanupResult result)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }

            try
            {
                foreach (var file in Directory.GetFiles(dir, searchPattern, SearchOption.AllDirectories))
                {
                    SafeDeleteFile(file, result);
                }
            }
            catch
            {
                // 忽略遍历异常
            }
        }

        private void SafeDeleteFile(string file, StorageCleanupResult result)
        {
            try
            {
                long length = SafeGetFileLength(file);
                File.Delete(file);
                result.DeletedFileCount++;
                result.FreedBytes += length;
            }
            catch
            {
                result.FailedFileCount++;
            }
        }

        private long SafeGetFileLength(string filePath)
        {
            try
            {
                return new FileInfo(filePath).Length;
            }
            catch
            {
                return 0;
            }
        }

        private static string SanitizeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Trim();
        }
    }
}