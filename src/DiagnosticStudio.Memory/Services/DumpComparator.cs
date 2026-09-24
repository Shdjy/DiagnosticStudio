using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiagnosticStudio.Core.Logging;
using DiagnosticStudio.Core.Models;
using DiagnosticStudio.Core.Storage;
using DiagnosticStudio.Memory.Managed;
using DiagnosticStudio.Memory.Native;

namespace DiagnosticStudio.Memory.Services
{
    /// <summary>
    /// 标准双 Dump 差分比较与增长分析引擎实现
    /// 整合托管堆 (CLR GC Heap)、原生虚拟内存段与线程代码泄漏定位
    /// </summary>
    public sealed class DumpComparator : IDumpComparator
    {
        private const string ModuleTag = "DumpComparator";
        private readonly IManagedHeapAnalyzer _managedHeapAnalyzer;
        private readonly INativeMemoryAnalyzer _nativeMemoryAnalyzer;
        private readonly IDiagnosticLogger _logger;

        public DumpComparator()
            : this(new ManagedHeapAnalyzer(DiagnosticLogger.Instance),
                   new NativeMemoryAnalyzer(DiagnosticLogger.Instance),
                   DiagnosticLogger.Instance)
        {
        }

        public DumpComparator(
            IManagedHeapAnalyzer managedHeapAnalyzer,
            INativeMemoryAnalyzer nativeMemoryAnalyzer,
            IDiagnosticLogger logger)
        {
            _managedHeapAnalyzer = managedHeapAnalyzer ?? throw new ArgumentNullException(nameof(managedHeapAnalyzer));
            _nativeMemoryAnalyzer = nativeMemoryAnalyzer ?? throw new ArgumentNullException(nameof(nativeMemoryAnalyzer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public DumpDiffResult Compare(DumpReportModel reportA, DumpReportModel reportB)
        {
            if (reportA == null)
            {
                throw new ArgumentNullException(nameof(reportA));
            }
            if (reportB == null)
            {
                throw new ArgumentNullException(nameof(reportB));
            }

            var result = new DumpDiffResult
            {
                DumpPathA = reportA.DumpPath,
                DumpPathB = reportB.DumpPath,
                FileSizeA = reportA.FileSizeBytes,
                FileSizeB = reportB.FileSizeBytes,
                ThreadCountA = reportA.Threads.Count,
                ThreadCountB = reportB.Threads.Count,
                ModuleCountA = reportA.Modules.Count,
                ModuleCountB = reportB.Modules.Count
            };

            // 1. 判断目标进程架构 (.NET 托管 vs Native 原生)
            bool hasClrModule = reportB.Modules.Any(m =>
                m.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase) ||
                m.ModuleName.Equals("clr.dll", StringComparison.OrdinalIgnoreCase));

            result.IsDotNetProcess = hasClrModule || _managedHeapAnalyzer.IsManagedDump(reportB.DumpPath);

            // 2. 深度分析 Managed Heap (托管堆对象 Top 增长排行)
            if (result.IsDotNetProcess)
            {
                try
                {
                    result.TopManagedGrowths = _managedHeapAnalyzer.CompareHeaps(reportA.DumpPath, reportB.DumpPath);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ModuleTag, $"分析托管堆增长失败: {ex.Message}");
                }
            }

            // 3. 深度分析 Native Memory (原生虚拟内存段与堆块增量排行)
            try
            {
                result.TopNativeGrowths = _nativeMemoryAnalyzer.CompareNativeRegions(
                    reportA.DumpPath,
                    reportB.DumpPath,
                    reportB.Threads);
            }
            catch (Exception ex)
            {
                _logger.Warning(ModuleTag, $"分析原生内存段失败: {ex.Message}");
            }

            // 4. 精准定位代码泄漏责任点 (结合线程栈、源码行号与调用帧特征)
            try
            {
                result.SuspectLocations = _nativeMemoryAnalyzer.LocateSuspectCode(
                    reportA,
                    reportB,
                    result.DeltaFileSizeBytes);

                result.PrimarySuspect = result.SuspectLocations.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.Warning(ModuleTag, $"定位代码泄漏点失败: {ex.Message}");
            }

            // 5. 比较模块状态 (用于排查动态加载的 DLL/插件)
            var mapA = reportA.Modules.ToDictionary(m => m.ModuleName, StringComparer.OrdinalIgnoreCase);
            var mapB = reportB.Modules.ToDictionary(m => m.ModuleName, StringComparer.OrdinalIgnoreCase);

            var allModuleNames = new HashSet<string>(mapA.Keys, StringComparer.OrdinalIgnoreCase);
            allModuleNames.UnionWith(mapB.Keys);

            foreach (var name in allModuleNames)
            {
                bool inA = mapA.TryGetValue(name, out var modA);
                bool inB = mapB.TryGetValue(name, out var modB);

                long szA = inA ? modA!.SizeOfImage : 0;
                long szB = inB ? modB!.SizeOfImage : 0;

                string status = "相同";
                if (!inA && inB)
                {
                    status = "新加载模块";
                }
                else if (inA && !inB)
                {
                    status = "已卸载模块";
                }

                result.TopModuleGrowths.Add(new ModuleGrowthItem
                {
                    ModuleName = name,
                    SizeA = szA,
                    SizeB = szB,
                    Status = status
                });
            }

            result.TopModuleGrowths = result.TopModuleGrowths
                .OrderByDescending(m => m.Status == "新加载模块" ? 1 : 0)
                .ThenByDescending(m => m.DeltaBytes)
                .ToList();

            // 6. 比较线程变化
            var threadIdsA = new HashSet<uint>(reportA.Threads.Select(t => t.ThreadId));
            var threadIdsB = new HashSet<uint>(reportB.Threads.Select(t => t.ThreadId));
            var newThreads = new List<ThreadInfo>();

            foreach (var tB in reportB.Threads)
            {
                if (!threadIdsA.Contains(tB.ThreadId))
                {
                    newThreads.Add(tB);
                    result.ThreadChanges.Add(new ThreadGrowthItem
                    {
                        ThreadId = tB.ThreadId,
                        Status = "新生线程 (New Thread)",
                        TopFrame = tB.TopFrameSummary
                    });
                }
                else if (result.PrimarySuspect != null && result.PrimarySuspect.ThreadId == tB.ThreadId)
                {
                    result.ThreadChanges.Add(new ThreadGrowthItem
                    {
                        ThreadId = tB.ThreadId,
                        Status = "🎯 疑似内存泄漏责任线程",
                        TopFrame = tB.TopFrameSummary
                    });
                }
            }

            foreach (var tA in reportA.Threads)
            {
                if (!threadIdsB.Contains(tA.ThreadId))
                {
                    result.ThreadChanges.Add(new ThreadGrowthItem
                    {
                        ThreadId = tA.ThreadId,
                        Status = "已终止线程 (Terminated)",
                        TopFrame = tA.TopFrameSummary
                    });
                }
            }

            // 7. 智能多维推导 (生成具备精确代码定位的 DiffFindings)

            // 推导 7.1: 精准代码泄漏定位诊断条目
            if (result.PrimarySuspect != null)
            {
                var p = result.PrimarySuspect;
                var suspectFinding = new DiagnosticFinding
                {
                    Level = result.DeltaFileSizeBytes >= 20 * 1024 * 1024 ? DiagnosticLevel.Critical : DiagnosticLevel.High,
                    Title = $"🎯 发现内存持续增长疑似代码责任点: {p.ModuleName}!{p.FunctionName}",
                    Description = $"在两份快照对比窗口内，进程私有内存物理净增 {result.DeltaFileSizeDisplay}。\n" +
                                  $"经启发式线程栈扫描与责任推导，已锁定处于暴增主路径上的责任代码为 {p.ModuleName}!{p.FunctionName}" +
                                  (!string.IsNullOrEmpty(p.SourceFile) && p.LineNumber > 0 ? $" (源文件: {Path.GetFileName(p.SourceFile)} 第 {p.LineNumber} 行)" : "") +
                                  $"。\n【归因详情】{p.DiagnosisDetail}",
                    Module = p.ModuleName,
                    Function = p.FunctionName,
                    SourceFile = p.SourceFile,
                    Line = p.LineNumber,
                    Confidence = p.Confidence
                };

                suspectFinding.Evidence.Add($"责任线程 ID: TID {p.ThreadId} ({p.ThreadRole})");
                suspectFinding.Evidence.Add($"责任函数: {p.ModuleName}!{p.FunctionName}");
                if (!string.IsNullOrEmpty(p.SourceFile) && p.LineNumber > 0)
                {
                    suspectFinding.Evidence.Add($"源码文件: {p.SourceFile} 第 {p.LineNumber} 行");
                }
                suspectFinding.Evidence.Add($"物理净增量: +{result.DeltaFileSizeDisplay}");
                if (result.TopNativeGrowths.Count > 0)
                {
                    var topSeg = result.TopNativeGrowths[0];
                    suspectFinding.Evidence.Add($"首要暴增内存段: {topSeg.BaseAddressHex} (净增 {topSeg.DeltaSizeDisplay}，状态: {topSeg.GrowthStatus})");
                }
                else if (result.IsDotNetProcess && result.TopManagedGrowths.Count > 0)
                {
                    var topManaged = result.TopManagedGrowths[0];
                    suspectFinding.Evidence.Add($"首要暴增托管类型: {topManaged.TypeName} (净增 {topManaged.DeltaSizeDisplay})");
                }
                suspectFinding.Evidence.Add($"置信度: {(p.Confidence * 100):F0}%");

                suspectFinding.Recommendations.Add("双击当前诊断卡片或点击快捷定位，可在【源码定位】中直接跳转至该用户函数的现场代码。");
                suspectFinding.Recommendations.Add("排查该函数内部是否存在未配对释放的堆内存指针 (new/malloc)、未清空的全局容器 (vector/list)、或未能正确触发垃圾回收的事件订阅。");

                suspectFinding.RelatedStack.AddRange(p.RelatedFrames);
                result.DiffFindings.Add(suspectFinding);
            }

            // 推导 7.1.1: 深度穿透新生线程的用户业务代码
            foreach (var tB in newThreads)
            {
                var userFrame = tB.Frames.FirstOrDefault(f => f.IsUserCode)
                             ?? tB.Frames.FirstOrDefault(f => !string.IsNullOrEmpty(f.SourceFile));

                if (userFrame != null)
                {
                    var threadFinding = new DiagnosticFinding
                    {
                        Level = DiagnosticLevel.High,
                        Title = $"🧵 新增活跃工作线程执行用户代码: {userFrame.FunctionName}",
                        Description = $"Dump B 相比 Dump A 新增了活跃线程 TID {tB.ThreadId}，且该线程正在执行用户业务代码 {userFrame.ModuleName}!{userFrame.FunctionName} (源文件: {Path.GetFileName(userFrame.SourceFile)} 第 {userFrame.LineNumber} 行)。\n" +
                                      "如果此线程为后台处理循环或生产者工作线程，请重点排查其分配的内存、队列缓存是否受控释放。",
                        Module = userFrame.ModuleName,
                        Function = userFrame.FunctionName,
                        SourceFile = userFrame.SourceFile,
                        Line = userFrame.LineNumber,
                        Confidence = 0.88
                    };

                    threadFinding.Evidence.Add($"新生线程 ID: TID {tB.ThreadId}");
                    threadFinding.Evidence.Add($"用户责任函数: {userFrame.ModuleName}!{userFrame.FunctionName}");
                    if (!string.IsNullOrEmpty(userFrame.SourceFile) && userFrame.LineNumber > 0)
                    {
                        threadFinding.Evidence.Add($"源码位置: {userFrame.SourceFile} 第 {userFrame.LineNumber} 行");
                    }
                    threadFinding.Recommendations.Add("双击当前诊断条目可直接在【源码定位】中跳转至该用户函数的现场代码。");
                    threadFinding.Recommendations.Add("排查该工作线程内的循环退出条件，以及循环体内申请的资源是否在每一轮循环后均被可靠释放。");

                    threadFinding.RelatedStack.AddRange(tB.Frames.Take(8));
                    result.DiffFindings.Add(threadFinding);
                    break;
                }
            }

            // 推导 7.2: 托管堆对象 Top 增长明细推导 (.NET)
            if (result.IsDotNetProcess && result.TopManagedGrowths.Count > 0)
            {
                var topType = result.TopManagedGrowths[0];
                if (topType.DeltaBytes > 1024 * 1024 || topType.DeltaCount > 1000)
                {
                    var managedFinding = new DiagnosticFinding
                    {
                        Level = DiagnosticLevel.High,
                        Title = $"📈 托管堆对象增量热点: {topType.TypeName} ({topType.DeltaSizeDisplay})",
                        Description = $"托管堆中对象类型 [{topType.TypeName}] 产生显著膨胀，在对比窗口内净增 {topType.DeltaSizeDisplay} (存活实例数增量: {topType.DeltaCountDisplay})。\n" +
                                      $"分类推导: {topType.Category}。线索分析: {topType.SuspectRootReason}",
                        Confidence = 0.92
                    };

                    managedFinding.Evidence.Add($"对象类型: {topType.TypeName}");
                    managedFinding.Evidence.Add($"堆体积变化: {topType.SizeADisplay} -> {topType.SizeBDisplay} (净增 {topType.DeltaSizeDisplay})");
                    managedFinding.Evidence.Add($"实例数量变化: {topType.CountA:N0} -> {topType.CountB:N0} (净增 {topType.DeltaCountDisplay})");
                    managedFinding.Evidence.Add($"类别特征: {topType.Category}");

                    managedFinding.Recommendations.Add($"切换至下方【堆与内存段增量明细】Tab 可查看完整的托管对象增长排行榜。");
                    managedFinding.Recommendations.Add($"请检查业务中持有该 [{topType.TypeName}] 的长生命周期集合或静态单例，添加淘汰或清空机制。");

                    result.DiffFindings.Add(managedFinding);
                }
            }

            // 推导 7.3: 原生虚拟内存段增量推导 (Native Heap)
            if (result.TopNativeGrowths.Count > 0)
            {
                var topRegion = result.TopNativeGrowths[0];
                if (topRegion.DeltaBytes > 5 * 1024 * 1024)
                {
                    var regionFinding = new DiagnosticFinding
                    {
                        Level = DiagnosticLevel.High,
                        Title = $"🧱 原生堆/私有内存段显著扩容: {topRegion.BaseAddressHex} ({topRegion.DeltaSizeDisplay})",
                        Description = $"原生私有段基地址 {topRegion.BaseAddressHex} 出现大体积净增 ({topRegion.DeltaSizeDisplay})。\n" +
                                      $"保护属性: {topRegion.ProtectDescription}，内存类型: {topRegion.TypeDescription}，状态: {topRegion.GrowthStatus}。\n" +
                                      $"该段通常用于 C/C++ 动态堆分配缓冲区 (malloc/new) 或私有数据缓存。",
                        Confidence = 0.88
                    };

                    regionFinding.Evidence.Add($"虚拟内存基地址: {topRegion.BaseAddressHex}");
                    regionFinding.Evidence.Add($"段大小变化: {topRegion.SizeADisplay} -> {topRegion.SizeBDisplay} (净增 {topRegion.DeltaSizeDisplay})");
                    regionFinding.Evidence.Add($"变动状态: {topRegion.GrowthStatus}");
                    if (!string.IsNullOrEmpty(topRegion.RelatedOwner))
                    {
                        regionFinding.Evidence.Add($"关联调用栈: {topRegion.RelatedOwner}");
                    }

                    regionFinding.Recommendations.Add("切换至下方【堆与内存段增量明细】Tab 查看所有膨胀的原生虚拟内存段分布。");
                    regionFinding.Recommendations.Add("可开启 GFlags / PageHeap / UMDH 工具跟踪该虚拟地址范围由哪条指令申请。");

                    result.DiffFindings.Add(regionFinding);
                }
            }

            // 推导 7.4: 宏观物理增长通用兜底
            long deltaBytes = result.DeltaFileSizeBytes;
            if (deltaBytes > 1024 * 1024 && result.DiffFindings.Count == 0)
            {
                double deltaMb = (double)deltaBytes / (1024 * 1024);
                double growthRate = (double)deltaBytes / Math.Max(1, result.FileSizeA) * 100;

                result.DiffFindings.Add(new DiagnosticFinding
                {
                    Level = deltaMb >= 30 ? DiagnosticLevel.Critical : DiagnosticLevel.High,
                    Title = $"🔴 目标进程发生私有内存显著物理净增 (+{deltaMb:F2} MB)",
                    Description = $"在两份快照对比窗口内，进程转储体积由 {result.FileSizeADisplay} 净增至 {result.FileSizeBDisplay} (净增 +{deltaMb:F2} MB，相对增长率 +{growthRate:F1}%)。",
                    Confidence = 0.85
                });
            }
            else if (deltaBytes <= 0 && result.DiffFindings.Count == 0)
            {
                result.DiffFindings.Add(new DiagnosticFinding
                {
                    Level = DiagnosticLevel.Info,
                    Title = "💡 双快照转储体积基本保持稳定 (未检出显著物理增量)",
                    Description = "Dump B 与 Dump A 之间的转储体积差异微弱。建议在目标程序继续运行产生显著增长后再进行对比分析。",
                    Confidence = 0.85
                });
            }

            // 推导 7.5: 线程数量失控膨胀预警
            if (result.DeltaThreadCount >= 5)
            {
                result.DiffFindings.Add(new DiagnosticFinding
                {
                    Level = DiagnosticLevel.Warning,
                    Title = $"🟡 线程数量异常增长 (+{result.DeltaThreadCount} 条线程)",
                    Description = $"Dump B 相比 Dump A 增加了 {result.DeltaThreadCount} 个新线程且未正常退出。可能存在线程池任务积压或持续创建未 Join 的后台线程引发线程栈资源泄漏。",
                    Confidence = 0.80
                });
            }

            // 按严重级别降序排序 (Critical -> High -> Warning -> Info)
            result.DiffFindings = result.DiffFindings.OrderByDescending(f => f.Level).ThenByDescending(f => f.Confidence).ToList();

            return result;
        }
    }
}