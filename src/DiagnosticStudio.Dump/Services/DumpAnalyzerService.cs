using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticStudio.Core.Logging;
using DiagnosticStudio.Core.Models;
using DiagnosticStudio.Core.Rules;
using DiagnosticStudio.Dump.Native;

namespace DiagnosticStudio.Dump.Services
{
    /// <summary>
    /// 标准 Dump 文件分析调度引擎实现
    /// </summary>
    public sealed class DumpAnalyzerService : IDumpAnalyzer
    {
        private readonly IRuleEngine _ruleEngine;
        private readonly ISuspectCodeLocator _codeLocator;
        private readonly IDiagnosticLogger _logger;

        public const string DefaultSymbolServer = "srv*C:\\Symbols*https://msdl.microsoft.com/download/symbols";

        public DumpAnalyzerService(IRuleEngine? ruleEngine = null, ISuspectCodeLocator? codeLocator = null, IDiagnosticLogger? logger = null)
        {
            _ruleEngine = ruleEngine ?? new DefaultRuleEngine();
            _codeLocator = codeLocator ?? new SuspectCodeLocator();
            _logger = logger ?? DiagnosticLogger.Instance;
        }

        public async Task<DumpReportModel> AnalyzeAsync(string dumpFilePath, string? symbolPath = null, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(dumpFilePath))
            {
                throw new FileNotFoundException($"指定的转储文件不存在: {dumpFilePath}", dumpFilePath);
            }

            string dumpDir = Path.GetDirectoryName(Path.GetFullPath(dumpFilePath)) ?? string.Empty;
            string localSymbolPrefix = !string.IsNullOrEmpty(dumpDir) 
                ? dumpDir + ";" + Path.Combine(dumpDir, "Symbols") + ";" 
                : string.Empty;
            string parentDir = !string.IsNullOrEmpty(dumpDir) ? (Path.GetDirectoryName(dumpDir) ?? string.Empty) : string.Empty;
            if (!string.IsNullOrEmpty(parentDir))
            {
                localSymbolPrefix += parentDir + ";" + Path.Combine(parentDir, "Symbols") + ";";
            }

            string effectiveSymbolPath = !string.IsNullOrWhiteSpace(symbolPath)
                ? (!string.IsNullOrEmpty(dumpDir) && symbolPath.Contains(dumpDir, StringComparison.OrdinalIgnoreCase) ? symbolPath : $"{localSymbolPrefix}{symbolPath}")
                : $"{localSymbolPrefix}{DefaultSymbolServer}";

            return await Task.Run(() =>
            {
                using var measure = _logger.Measure("DumpAnalyzer", $"解析文件 {Path.GetFileName(dumpFilePath)}");

                var fileInfo = new FileInfo(dumpFilePath);
                var report = new DumpReportModel
                {
                    DumpPath = Path.GetFullPath(dumpFilePath),
                    FileSizeBytes = fileInfo.Length,
                    AnalysisTime = DateTime.Now
                };

                _logger.Info("DumpAnalyzer", $"正在打开 Native Dump 会话，符号路径: {effectiveSymbolPath}");

                int openResult = NativeMethods.OpenDumpSession(dumpFilePath, effectiveSymbolPath, out IntPtr session);
                if (openResult != 0 || session == IntPtr.Zero)
                {
                    string errMsg = $"无法打开 Dump 文件，错误码: 0x{openResult:X8}";
                    _logger.Error("DumpAnalyzer", errMsg);
                    throw new InvalidOperationException(errMsg);
                }

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    unsafe
                    {

                    // 1. 读取系统信息
                    NativeSystemInfo sysInfo;
                        _logger.Info("DumpAnalyzer", $"[Native会话有效] session=0x{session.ToInt64():X16}"); int sysRes = NativeMethods.GetDumpSystemInfo(session, &sysInfo); _logger.Info("DumpAnalyzer", $"[GetDumpSystemInfo] 结果: {sysRes}"); if (sysRes == 0)
                    {
                        report.SystemInfo = new ProcessSystemInfo
                        {
                            ProcessorArchitecture = sysInfo.ProcessorArchitecture,
                            NumberOfProcessors = sysInfo.NumberOfProcessors,
                            MajorVersion = sysInfo.MajorVersion,
                            MinorVersion = sysInfo.MinorVersion,
                            BuildNumber = sysInfo.BuildNumber,
                            CSDVersion = sysInfo.CSDVersion ?? string.Empty
                        };
                    }

                    // 2. 读取异常记录
                    NativeExceptionInfo expInfo;
                        int expRes = NativeMethods.GetDumpExceptionInfo(session, &expInfo); _logger.Info("DumpAnalyzer", $"[GetDumpExceptionInfo] 结果: {expRes}"); if (expRes == 0)
                    {
                        var expModel = new ExceptionInfo
                        {
                            ExceptionCode = expInfo.ExceptionCode,
                            ExceptionFlags = expInfo.ExceptionFlags,
                            ExceptionAddress = expInfo.ExceptionAddress,
                            ThreadId = expInfo.ThreadId
                        };

                        if (expInfo.ExceptionInformation != null && expInfo.NumberParameters > 0)
                        {
                            for (int i = 0; i < expInfo.NumberParameters && i < expInfo.ExceptionInformation.Length; i++)
                            {
                                expModel.Parameters.Add(expInfo.ExceptionInformation[i]);
                            }
                        }

                        report.Exception = expModel;
                    }

                    // 3. 读取加载模块
                    int modRes = NativeMethods.GetDumpModuleCount(session, out int modCount); _logger.Info("DumpAnalyzer", $"[GetDumpModuleCount] 结果: {modRes}, modCount={modCount}"); if (modRes == 0)
                    {
                        for (int i = 0; i < modCount; i++)
                        {
                            NativeModuleInfo modInfo;
                                if (NativeMethods.GetDumpModule(session, i, &modInfo) == 0)
                            {
                                report.Modules.Add(new ModuleInfo
                                {
                                    BaseOfImage = modInfo.BaseOfImage,
                                    SizeOfImage = modInfo.SizeOfImage,
                                    ModuleName = modInfo.ModuleName ?? string.Empty,
                                    ImageName = modInfo.ImageName ?? string.Empty,
                                    LoadedPdbName = modInfo.LoadedPdbName ?? string.Empty,
                                    IsSymbolLoaded = modInfo.IsSymbolLoaded != 0
                                });
                            }
                        }
                    }

                    // 4. 读取线程与调用栈
                    int thrRes = NativeMethods.GetDumpThreadCount(session, out int threadCount); _logger.Info("DumpAnalyzer", $"[GetDumpThreadCount] 结果: {thrRes}, threadCount={threadCount}"); if (thrRes == 0)
                    {
                        for (int i = 0; i < threadCount; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (NativeMethods.GetDumpThreadId(session, i, out uint threadId) == 0)
                            {
                                var threadModel = new ThreadInfo
                                {
                                    Index = i,
                                    ThreadId = threadId
                                };

                                // 读取至多 64 层调用栈
                                var framesBuffer = new NativeStackFrameInfo[64];
                                fixed (NativeStackFrameInfo* pFrames = framesBuffer)
                                {
                                    if (NativeMethods.GetDumpThreadStackTrace(session, threadId, framesBuffer.Length, pFrames, out int actualFrames) == 0)
                                {
                                    for (int f = 0; f < actualFrames; f++)
                                    {
                                        var rawFrame = framesBuffer[f];
                                        threadModel.Frames.Add(new StackFrameInfo
                                        {
                                            InstructionOffset = rawFrame.InstructionOffset,
                                            ReturnOffset = rawFrame.ReturnOffset,
                                            FrameOffset = rawFrame.FrameOffset,
                                            StackOffset = rawFrame.StackOffset,
                                            ModuleName = rawFrame.ModuleName ?? string.Empty,
                                            FunctionName = rawFrame.FunctionName ?? string.Empty,
                                            SourceFile = rawFrame.SourceFile ?? string.Empty,
                                            LineNumber = rawFrame.LineNumber,
                                            Displacement = rawFrame.Displacement
                                        });
                                    }
                                }

                                report.Threads.Add(threadModel);
                                }
                            }
                        }
                    }

                    // 5. 智能可疑代码分析与标记（自动标红/标黄）
                    _codeLocator.MarkSuspectFrames(report.Threads, report.Exception?.ThreadId);

                    // 6. 执行多维规则引擎
                    report.Findings = _ruleEngine.Analyze(report);

                    _logger.Info("DumpAnalyzer", $"解析完成，共提取 {report.Modules.Count} 模块，{report.Threads.Count} 线程，推导产生 {report.Findings.Count} 处诊断项。");

                    return report;
                    }
                }
                finally
                {
                    NativeMethods.CloseDumpSession(session);
                }
            }, cancellationToken);
        }
    }
}




