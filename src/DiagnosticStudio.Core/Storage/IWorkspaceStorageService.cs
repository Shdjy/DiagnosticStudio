using System;

namespace DiagnosticStudio.Core.Storage
{
    /// <summary>
    /// 诊断工作区数据存储与生命周期管理服务接口
    /// </summary>
    public interface IWorkspaceStorageService
    {
        /// <summary>
        /// 诊断数据工作区根目录完整路径
        /// </summary>
        string RootDataDirectory { get; }

        /// <summary>
        /// 获取快照 Dump 存储专用目录（确保目录已创建）
        /// </summary>
        /// <returns>快照目录物理路径</returns>
        string GetSnapshotsDirectory();

        /// <summary>
        /// 获取系统运行日志存储专用目录（确保目录已创建）
        /// </summary>
        /// <returns>日志目录物理路径</returns>
        string GetLogsDirectory();

        /// <summary>
        /// 获取诊断报告生成专用目录（确保目录已创建）
        /// </summary>
        /// <returns>报告目录物理路径</returns>
        string GetReportsDirectory();

        /// <summary>
        /// 获取当前活动运行日志文件的物理路径 (DiagnosticsData/Logs/DiagnosticStudio_yyyyMMdd.log)
        /// </summary>
        /// <returns>日志文件物理路径</returns>
        string GetCurrentLogFilePath();

        /// <summary>
        /// 格式化生成包含进程名、PID 与时间戳的规范快照 Dump 文件物理路径
        /// 格式: DiagnosticsData/Snapshots/{ProcessName}_{PID}_{yyyyMMdd_HHmmss}.dmp
        /// </summary>
        /// <param name="processName">目标进程名称（如 CppCrashTarget）</param>
        /// <param name="processId">目标进程系统 PID</param>
        /// <returns>规范化的快照 Dump 文件完整路径</returns>
        string GenerateSnapshotFilePath(string? processName, int processId);

        /// <summary>
        /// 统计当前工作区内所有中间文件（快照、旧日志、临时报告）的数量与占用空间
        /// </summary>
        /// <returns>存储统计摘要信息</returns>
        StorageCleanupSummary GetCleanupSummary();

        /// <summary>
        /// 清理工作区内的中间文件
        /// </summary>
        /// <param name="cleanSnapshots">是否清理历史快照文件</param>
        /// <param name="cleanLogs">是否清理历史日志文件（排除当天正在写入的文件）</param>
        /// <param name="cleanReports">是否清理临时导出的报告文件</param>
        /// <returns>清理操作统计结果</returns>
        StorageCleanupResult CleanIntermediateFiles(bool cleanSnapshots = true, bool cleanLogs = false, bool cleanReports = true);

        /// <summary>
        /// 在 Windows 文件资源管理器中定位并打开指定目录
        /// </summary>
        /// <param name="subDirectoryPath">指定的子目录路径，若为 null 则打开工作区根目录</param>
        void OpenDirectoryInExplorer(string? subDirectoryPath = null);
    }
}