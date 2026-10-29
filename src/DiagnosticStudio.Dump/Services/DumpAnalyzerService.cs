using System;
using System.IO;
using System.Linq;
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
    /// 具备 Dump 完整性自检、多流质量评估、符号匹配追踪与规则审计能力
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
                    AnalysisTime = DateTime.Now,
                    AnalyzerVersion = "2.1.0",
                    RuleEngineVersion = "2.1.0"
                };

                _logger.Info("DumpAnalyzer", $"正在打开 Native Dump 会话，符号路径: {effectiveSymbolPath}");

                int openResult = NativeMethods.OpenDumpSession(dumpFilePath, effectiveSymbolPath, out IntPtr session);
                if (openResult != 0 || session == IntPtr.Zero)
                {
                    report.Integrity.Status = DumpIntegrityStatus.Corrupted;
                    report.Integrity.Warnings.Add($"Native会话打开失败，底层返回值: 0x{openResult:X8}");
                    report.OverallStatus = AnalysisOverallStatus.Failed;
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
                        int sysRes = NativeMethods.GetDumpSystemInfo(session, &sysInfo);
                        if (sysRes == 0)
                        {
                            report.Integrity.HasSystemInfoStream = true;
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
                        else
                        {
                            report.Integrity.HasSystemInfoStream = false;
                            report.Integrity.Warnings.Add("MiniDump 系统信息流缺失或无法读取。");
                        }

                        // 2. 读取异常记录
                        NativeExceptionInfo expInfo;
                        int expRes = NativeMethods.GetDumpExceptionInfo(session, &expInfo);
                        if (expRes == 0)
                        {
                            report.Integrity.HasExceptionStream = true;
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
                        else
                        {
                            report.Integrity.HasExceptionStream = false;
                            // 无异常流通常是用户主动抓取的快照或挂起快照
                        }

                        // 3. 读取加载模块
                        int modRes = NativeMethods.GetDumpModuleCount(session, out int modCount);
                        if (modRes == 0)
                        {
                            report.Integrity.HasModuleListStream = true;
                            for (int i = 0; i < modCount; i++)
                            {
                                NativeModuleInfo modInfo;
                                if (NativeMethods.GetDumpModule(session, i, &modInfo) == 0)
                                {
                                    bool isSymLoaded = modInfo.IsSymbolLoaded != 0;
                                    string loadedPdb = modInfo.LoadedPdbName ?? string.Empty;

                                    var modModel = new ModuleInfo
                                    {
                                        BaseOfImage = modInfo.BaseOfImage,
                                        SizeOfImage = modInfo.SizeOfImage,
                                        ModuleName = modInfo.ModuleName ?? string.Empty,
                                        ImageName = modInfo.ImageName ?? string.Empty,
                                        LoadedPdbName = loadedPdb,
                                        IsSymbolLoaded = isSymLoaded
                                    };

                                    // 符号质量评估
                                    if (isSymLoaded && !string.IsNullOrEmpty(loadedPdb))
                                    {
                                        modModel.SymbolQuality.Status = loadedPdb.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                                            ? SymbolQualityStatus.MatchedPdb
                                            : SymbolQualityStatus.ExportSymbolsOnly;
                                        modModel.SymbolQuality.LoadedPdbPath = loadedPdb;
                                    }
                                    else
                                    {
                                        modModel.SymbolQuality.Status = SymbolQualityStatus.NoSymbols;
                                    }

                                    report.Modules.Add(modModel);
                                }
                            }
                        }
                        else
                        {
                            report.Integrity.HasModuleListStream = false;
                            report.Integrity.Warnings.Add("MiniDump 模块列表流缺失。");
                        }

                        // 4. 读取线程与调用栈
                        int thrRes = NativeMethods.GetDumpThreadCount(session, out int threadCount);
                        if (thrRes == 0)
                        {
                            report.Integrity.HasThreadListStream = true;
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
                                                bool hasSource = !string.IsNullOrEmpty(rawFrame.SourceFile) && rawFrame.LineNumber > 0;
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

                                                // 回填模块是否有行号符号
                                                if (hasSource && !string.IsNullOrEmpty(rawFrame.ModuleName))
                                                {
                                                    var targetMod = report.Modules.FirstOrDefault(m => m.ModuleName.Equals(rawFrame.ModuleName, StringComparison.OrdinalIgnoreCase));
                                                    if (targetMod != null)
                                                    {
                                                        targetMod.SymbolQuality.HasSourceLineInfo = true;
                                                    }
                                                }
                                            }
                                        }

                                        report.Threads.Add(threadModel);
                                    }
                                }
                            }
                        }
                        else
                        {
                            report.Integrity.HasThreadListStream = false;
                            report.Integrity.Warnings.Add("MiniDump 线程列表流缺失。");
                        }

                        // 5. 检查转储会话是否通过启发式引擎进行了容错修复与重建
                        if (NativeMethods.IsDumpHeuristicallyRecovered(session, out int isRecovered) == 0 && isRecovered != 0)
                        {
                            report.IsRecovered = true;
                            report.Integrity.Status = DumpIntegrityStatus.HeuristicallyRecovered;
                            report.Integrity.RecoveryNotes.Add("检测到底层流索引未闭合（全0），已通过启发式物理特征扫描自动重建恢复。");
                            _logger.Warning("DumpAnalyzer", "转储文件由于流目录未收尾(全0)被检测为截断损坏，已成功通过启发式物理特征引擎自愈重建！");
                        }
                        else if (!report.Integrity.HasModuleListStream || !report.Integrity.HasThreadListStream)
                        {
                            report.Integrity.Status = DumpIntegrityStatus.PartiallyAvailable;
                        }
                        else
                        {
                            report.Integrity.Status = DumpIntegrityStatus.Complete;
                        }

                        // 6. 智能可疑代码分析与标记（自动标红/标黄）
                        _codeLocator.MarkSuspectFrames(report.Threads, report.Exception?.ThreadId);

                        // 7. 执行多维规则引擎（执行结果与审计信息自动写入 report）
                        report.Findings = _ruleEngine.Analyze(report);

                        _logger.Info("DumpAnalyzer", $"解析完成，完整性: {report.Integrity.StatusBadge}，共提取 {report.Modules.Count} 模块，{report.Threads.Count} 线程，推导产生 {report.Findings.Count} 处诊断项。");

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
