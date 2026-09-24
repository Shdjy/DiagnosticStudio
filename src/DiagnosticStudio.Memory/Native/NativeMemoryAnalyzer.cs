using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiagnosticStudio.Core.Logging;
using DiagnosticStudio.Core.Models;
using DiagnosticStudio.Core.Storage;

namespace DiagnosticStudio.Memory.Native
{
    /// <summary>
    /// 标准原生内存段与堆分配差分分析器实现
    /// 基于标准 MiniDump 流解析与线程调用栈启发式推导
    /// </summary>
    public sealed class NativeMemoryAnalyzer : INativeMemoryAnalyzer
    {
        private const string ModuleTag = "NativeMemory";
        private readonly IDiagnosticLogger _logger;

        private const uint MINIDUMP_SIGNATURE = 0x504D444D; // 'MDMP' (0x504D444D)
        private const uint Memory64ListStream = 9;
        private const uint MemoryInfoListStream = 16;

        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_PRIVATE = 0x20000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint PAGE_EXECUTE_READ = 0x20;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;

        public NativeMemoryAnalyzer(IDiagnosticLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public List<NativeRegionGrowthItem> CompareNativeRegions(string dumpPathA, string dumpPathB, IReadOnlyList<ThreadInfo> threadsB)
        {
            var results = new List<NativeRegionGrowthItem>();

            if (!File.Exists(dumpPathA) || !File.Exists(dumpPathB))
            {
                return results;
            }

            try
            {
                var regionsA = ReadMemoryRegionsFromDump(dumpPathA);
                var regionsB = ReadMemoryRegionsFromDump(dumpPathB);

                // 若 dump 文件没有完整的 MemoryInfo/Memory64 流，安全降级
                if (regionsB.Count == 0)
                {
                    return results;
                }

                var mapA = regionsA.ToDictionary(r => r.BaseAddress);

                foreach (var regB in regionsB)
                {
                    // 仅关注已提交 (Commit) 的内存
                    if ((regB.State & MEM_COMMIT) == 0 && regB.State != 0)
                    {
                        continue;
                    }

                    bool inA = mapA.TryGetValue(regB.BaseAddress, out var regA);
                    long sizeA = inA ? (long)regA.RegionSize : 0;
                    long sizeB = (long)regB.RegionSize;
                    long delta = sizeB - sizeA;

                    // 仅收录产生增量或者单个提交体积显著 (> 256KB) 的段
                    if (delta <= 0 && sizeB < 256 * 1024)
                    {
                        continue;
                    }

                    string status = !inA ? "新增分配段" : (delta > 0 ? "扩容段" : "保持");
                    string protectStr = FormatProtect(regB.Protect);
                    string typeStr = FormatType(regB.Type);
                    string suspectCategory = InferRegionCategory(regB.Protect, regB.Type, sizeB);

                    // 尝试匹配是否属于某个线程的调用栈空间
                    string owner = MatchThreadOrModule(regB.BaseAddress, regB.RegionSize, threadsB);

                    results.Add(new NativeRegionGrowthItem
                    {
                        BaseAddressHex = $"0x{regB.BaseAddress:X16}",
                        BaseAddress = regB.BaseAddress,
                        SizeA = sizeA,
                        SizeB = sizeB,
                        GrowthStatus = status,
                        ProtectDescription = protectStr,
                        TypeDescription = typeStr,
                        SuspectCategory = suspectCategory,
                        RelatedOwner = owner
                    });
                }

                // 优先按增量降序排序，增量相同时按当前大小降序
                return results.OrderByDescending(r => r.DeltaBytes)
                              .ThenByDescending(r => r.SizeB)
                              .Take(50)
                              .ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ModuleTag, $"原生内存段差分分析失败: {ex.Message}", ex);
                return results;
            }
        }

        private static bool IsCompilerInternalSource(string? sourceFile, string? funcName)
        {
            if (!string.IsNullOrEmpty(sourceFile))
            {
                string s = sourceFile.ToLowerInvariant();
                if (s.Contains(@"\msvc\") || s.Contains(@"\vctools\") || s.Contains(@"\crt\") ||
                    s.Contains(@"\include\thread") || s.Contains(@"\include\chrono") || s.Contains(@"\include\__msvc") ||
                    s.EndsWith(".inl", StringComparison.OrdinalIgnoreCase) || s.Contains(@"\stl\src\"))
                {
                    return true;
                }
            }
            if (!string.IsNullOrEmpty(funcName))
            {
                if (funcName.StartsWith("std::", StringComparison.OrdinalIgnoreCase) ||
                    funcName.StartsWith("__scrt", StringComparison.OrdinalIgnoreCase) ||
                    funcName.StartsWith("ILT+", StringComparison.OrdinalIgnoreCase) ||
                    funcName.StartsWith("_Thrd_", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public List<SuspectCodeLocation> LocateSuspectCode(DumpReportModel reportA, DumpReportModel reportB, long deltaBytes)
        {
            var suspects = new List<SuspectCodeLocation>();

            if (reportB == null || reportB.Threads.Count == 0)
            {
                return suspects;
            }

            // 收集基线 Dump A 中的线程 ID 集合
            var threadIdsA = new HashSet<uint>(reportA?.Threads.Select(t => t.ThreadId) ?? Enumerable.Empty<uint>());

            // 对 Dump B 中的所有活跃线程进行综合嫌疑打分
            var scoredThreads = new List<(ThreadInfo Thread, int Score, StackFrameInfo? BestFrame, string Reason)>();

            foreach (var thread in reportB.Threads)
            {
                int score = 0;
                string reason = string.Empty;
                bool isNewThread = !threadIdsA.Contains(thread.ThreadId);

                if (isNewThread)
                {
                    score += 20; // 新生线程有基础分
                }

                // 扫描线程调用栈帧 (优先锁定用户自身编写的业务源码文件)
                StackFrameInfo? bestUserFrame = null;
                int bestFramePriority = 0; // 0: None, 1: IsUserCode, 2: STL/CRT internal, 3: Pure User Business Code
                bool hasAllocationCall = false;
                bool hasExecutionLoop = false;

                foreach (var frame in thread.Frames)
                {
                    string func = frame.FunctionName ?? string.Empty;
                    string mod = frame.ModuleName ?? string.Empty;
                    string src = frame.SourceFile ?? string.Empty;

                    // 严密检查：排除系统底层库
                    if (mod.Equals("ntdll", StringComparison.OrdinalIgnoreCase) ||
                        mod.Equals("kernel32", StringComparison.OrdinalIgnoreCase) ||
                        mod.Equals("kernelbase", StringComparison.OrdinalIgnoreCase) ||
                        mod.Equals("ucrtbase", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool hasSource = !string.IsNullOrEmpty(src) && frame.LineNumber > 0;
                    bool isCompilerInternal = IsCompilerInternalSource(src, func);

                    // 1. 最高优先级：用户编写的真实业务源码 (非 STL/CRT，具备 SourceFile 与 LineNumber)
                    if (hasSource && !isCompilerInternal)
                    {
                        if (bestFramePriority < 3)
                        {
                            bestUserFrame = frame;
                            bestFramePriority = 3;
                            score += 80;
                        }
                    }
                    else if (hasSource && bestFramePriority < 2)
                    {
                        bestUserFrame = frame;
                        bestFramePriority = 2;
                        score += 40;
                    }
                    else if (frame.IsUserCode && !isCompilerInternal && bestFramePriority < 1)
                    {
                        bestUserFrame = frame;
                        bestFramePriority = 1;
                        score += 20;
                    }

                    // 2. 检查内存分配或容器增长特征
                    if (func.Contains("malloc", StringComparison.OrdinalIgnoreCase) ||
                        func.Contains("HeapAlloc", StringComparison.OrdinalIgnoreCase) ||
                        func.Contains("operator new", StringComparison.OrdinalIgnoreCase) ||
                        func.Contains("vector<", StringComparison.OrdinalIgnoreCase) ||
                        func.Contains("push_back", StringComparison.OrdinalIgnoreCase) ||
                        func.Contains("Allocate", StringComparison.OrdinalIgnoreCase) ||
                        func.Contains("Buffer", StringComparison.OrdinalIgnoreCase) ||
                        func.Contains("Leak", StringComparison.OrdinalIgnoreCase))
                    {
                        hasAllocationCall = true;
                        score += 30;
                    }

                    // 3. 检查循环或业务执行特征
                    if (func.Contains("Execute", StringComparison.OrdinalIgnoreCase) ||
                        func.Contains("Run", StringComparison.OrdinalIgnoreCase) ||
                        func.Contains("Worker", StringComparison.OrdinalIgnoreCase) ||
                        func.Contains("Process", StringComparison.OrdinalIgnoreCase) ||
                        func.Contains("Loop", StringComparison.OrdinalIgnoreCase) ||
                        func.Contains("Scenario", StringComparison.OrdinalIgnoreCase))
                    {
                        hasExecutionLoop = true;
                        score += 25;
                    }
                }

                // 排除纯操作系统内核空闲等待线程
                if (bestUserFrame == null && !hasAllocationCall && !hasExecutionLoop)
                {
                    var top = thread.Frames.FirstOrDefault();
                    if (top != null && (top.FunctionName.Contains("ZwWaitForWorkViaWorkerFactory") ||
                                       top.FunctionName.Contains("TppWorkerThread") ||
                                       top.FunctionName.Contains("NtWaitForAlertByThreadId")))
                    {
                        score -= 50; // 系统空闲等待线程，大幅降分
                    }
                }

                if (bestUserFrame != null)
                {
                    if (hasAllocationCall || hasExecutionLoop)
                    {
                        reason = $"线程 TID {thread.ThreadId} 正在执行用户核心业务逻辑 {bestUserFrame.FunctionName}，并处于高频分配/循环路径中。";
                    }
                    else
                    {
                        reason = $"线程 TID {thread.ThreadId} 正在执行用户代码 {bestUserFrame.FunctionName}。";
                    }
                    scoredThreads.Add((thread, score, bestUserFrame, reason));
                }
                else if (hasAllocationCall)
                {
                    var topFrame = thread.Frames.FirstOrDefault(f => !f.FunctionName.Contains("ntdll") && !f.FunctionName.Contains("KERNELBASE"))
                                ?? thread.Frames.FirstOrDefault();
                    if (topFrame != null)
                    {
                        reason = $"线程 TID {thread.ThreadId} 正在调用底层内存分配函数 {topFrame.FunctionName}。";
                        scoredThreads.Add((thread, score, topFrame, reason));
                    }
                }
            }

            // 按嫌疑评分降序排列
            var candidates = scoredThreads.OrderByDescending(x => x.Score).ToList();

            foreach (var item in candidates.Take(5))
            {
                if (item.BestFrame == null)
                {
                    continue;
                }

                double confidence = Math.Min(0.95, 0.65 + item.Score * 0.003);
                if (deltaBytes > 10 * 1024 * 1024)
                {
                    confidence = Math.Min(0.98, confidence + 0.05);
                }

                var location = new SuspectCodeLocation
                {
                    ThreadId = item.Thread.ThreadId,
                    ThreadRole = (item.Thread.Index == 0 ? "主线程 (UI/调度)" : "业务工作线程 (Worker)"),
                    ModuleName = item.BestFrame.ModuleName,
                    FunctionName = item.BestFrame.FunctionName,
                    SourceFile = item.BestFrame.SourceFile,
                    LineNumber = item.BestFrame.LineNumber,
                    Confidence = confidence,
                    DiagnosisDetail = item.Reason + (deltaBytes > 0 ? $" 伴随私有内存净增 +{StorageCleanupSummary.FormatBytes(deltaBytes)}。" : ""),
                    RelatedFrames = item.Thread.Frames.Take(10).ToList()
                };

                suspects.Add(location);
            }

            return suspects;
        }

        #region MiniDump 解析核心辅助

        private struct RawMemoryRegion
        {
            public ulong BaseAddress;
            public ulong RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        private List<RawMemoryRegion> ReadMemoryRegionsFromDump(string dumpPath)
        {
            var list = new List<RawMemoryRegion>();

            try
            {
                using var fs = new FileStream(dumpPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new BinaryReader(fs);

                if (fs.Length < 32)
                {
                    return list;
                }

                uint sig = reader.ReadUInt32();
                if (sig != MINIDUMP_SIGNATURE)
                {
                    return list;
                }

                uint version = reader.ReadUInt32();
                uint streamCount = reader.ReadUInt32();
                uint streamDirRva = reader.ReadUInt32();

                fs.Seek(streamDirRva, SeekOrigin.Begin);

                long memoryInfoRva = -1;
                long memoryInfoSize = 0;
                long memory64Rva = -1;

                for (int i = 0; i < streamCount; i++)
                {
                    if (fs.Position + 12 > fs.Length) break;
                    uint sType = reader.ReadUInt32();
                    uint sSize = reader.ReadUInt32();
                    uint sRva = reader.ReadUInt32();

                    if (sType == MemoryInfoListStream)
                    {
                        memoryInfoRva = sRva;
                        memoryInfoSize = sSize;
                    }
                    else if (sType == Memory64ListStream)
                    {
                        memory64Rva = sRva;
                    }
                }

                // 优先解析 MemoryInfoListStream (包含 State, Protect, Type 完整元数据)
                if (memoryInfoRva > 0 && memoryInfoRva < fs.Length)
                {
                    fs.Seek(memoryInfoRva, SeekOrigin.Begin);
                    uint sizeOfHeader = reader.ReadUInt32();
                    uint sizeOfEntry = reader.ReadUInt32();
                    ulong numberOfEntries = reader.ReadUInt64();

                    for (ulong i = 0; i < numberOfEntries; i++)
                    {
                        long entryStart = fs.Position;
                        if (entryStart + sizeOfEntry > fs.Length) break;

                        ulong baseAddr = reader.ReadUInt64();
                        ulong allocBase = reader.ReadUInt64();
                        uint allocProt = reader.ReadUInt32();
                        uint align1 = reader.ReadUInt32();
                        ulong regionSize = reader.ReadUInt64();
                        uint state = reader.ReadUInt32();
                        uint protect = reader.ReadUInt32();
                        uint type = reader.ReadUInt32();

                        list.Add(new RawMemoryRegion
                        {
                            BaseAddress = baseAddr,
                            RegionSize = regionSize,
                            State = state,
                            Protect = protect,
                            Type = type
                        });

                        fs.Seek(entryStart + sizeOfEntry, SeekOrigin.Begin);
                    }

                    return list;
                }

                // 回退解析 Memory64ListStream
                if (memory64Rva > 0 && memory64Rva < fs.Length)
                {
                    fs.Seek(memory64Rva, SeekOrigin.Begin);
                    ulong rangeCount = reader.ReadUInt64();
                    ulong baseRva = reader.ReadUInt64();

                    for (ulong i = 0; i < rangeCount; i++)
                    {
                        if (fs.Position + 16 > fs.Length) break;
                        ulong startAddr = reader.ReadUInt64();
                        ulong dataSize = reader.ReadUInt64();

                        list.Add(new RawMemoryRegion
                        {
                            BaseAddress = startAddr,
                            RegionSize = dataSize,
                            State = MEM_COMMIT,
                            Protect = PAGE_READWRITE,
                            Type = MEM_PRIVATE
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ModuleTag, $"读取转储文件内存流异常 ({Path.GetFileName(dumpPath)}): {ex.Message}");
            }

            return list;
        }

        private static string FormatProtect(uint protect)
        {
            return protect switch
            {
                0x01 => "PAGE_NOACCESS",
                0x02 => "PAGE_READONLY",
                0x04 => "PAGE_READWRITE",
                0x08 => "PAGE_WRITECOPY",
                0x10 => "PAGE_EXECUTE",
                0x20 => "PAGE_EXECUTE_READ",
                0x40 => "PAGE_EXECUTE_READWRITE",
                _ => $"0x{protect:X2}"
            };
        }

        private static string FormatType(uint type)
        {
            return type switch
            {
                0x1000000 => "MEM_IMAGE (可执行映像)",
                0x40000 => "MEM_MAPPED (共享内存映射)",
                0x20000 => "MEM_PRIVATE (原生堆/私有页)",
                _ => $"0x{type:X}"
            };
        }

        private static string InferRegionCategory(uint protect, uint type, long size)
        {
            if (type == 0x20000 && protect == PAGE_READWRITE)
            {
                if (size >= 10 * 1024 * 1024)
                {
                    return "大容量原生堆缓冲区 (malloc/new/大内存块)";
                }
                return "原生私有堆块 (malloc/HeapAlloc)";
            }
            if (protect == PAGE_EXECUTE_READ || protect == PAGE_EXECUTE_READWRITE)
            {
                return "代码指令执行段 (Text/Code)";
            }
            if (type == 0x40000)
            {
                return "文件或IPC共享内存映射";
            }
            return "通用虚拟内存段";
        }

        private static string MatchThreadOrModule(ulong baseAddress, ulong regionSize, IReadOnlyList<ThreadInfo> threads)
        {
            if (threads == null) return string.Empty;

            foreach (var t in threads)
            {
                var top = t.Frames.FirstOrDefault();
                if (top != null)
                {
                    // 若栈顶指针或指令地址落入该段区间
                    if (top.InstructionOffset >= baseAddress && top.InstructionOffset < baseAddress + regionSize)
                    {
                        return $"关联线程 TID {t.ThreadId} ({top.ModuleName}!{top.FunctionName})";
                    }
                }
            }
            return string.Empty;
        }

        #endregion
    }
}
