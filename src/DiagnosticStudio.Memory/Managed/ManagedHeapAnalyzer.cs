using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiagnosticStudio.Core.Logging;
using DiagnosticStudio.Core.Models;
using Microsoft.Diagnostics.Runtime;

namespace DiagnosticStudio.Memory.Managed
{
    /// <summary>
    /// 标准托管堆差分分析器实现 (基于 Microsoft.Diagnostics.Runtime ClrMD)
    /// </summary>
    public sealed class ManagedHeapAnalyzer : IManagedHeapAnalyzer
    {
        private const string ModuleTag = "ManagedHeap";
        private readonly IDiagnosticLogger _logger;

        public ManagedHeapAnalyzer(IDiagnosticLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool IsManagedDump(string dumpPath)
        {
            if (string.IsNullOrWhiteSpace(dumpPath) || !File.Exists(dumpPath))
            {
                return false;
            }

            try
            {
                using var dataTarget = DataTarget.LoadDump(dumpPath);
                return dataTarget.ClrVersions != null && dataTarget.ClrVersions.Length > 0;
            }
            catch (Exception ex)
            {
                _logger.Warning(ModuleTag, $"检测托管 Dump 运行时异常 ({Path.GetFileName(dumpPath)}): {ex.Message}");
                return false;
            }
        }

        public List<HeapObjectGrowthItem> CompareHeaps(string dumpPathA, string dumpPathB)
        {
            var results = new List<HeapObjectGrowthItem>();

            if (!File.Exists(dumpPathA) || !File.Exists(dumpPathB))
            {
                return results;
            }

            try
            {
                var statsA = CollectHeapHistogram(dumpPathA);
                var statsB = CollectHeapHistogram(dumpPathB);

                if (statsA.Count == 0 && statsB.Count == 0)
                {
                    return results;
                }

                var allTypes = new HashSet<string>(statsA.Keys, StringComparer.Ordinal);
                allTypes.UnionWith(statsB.Keys);

                foreach (var typeName in allTypes)
                {
                    statsA.TryGetValue(typeName, out var valA);
                    statsB.TryGetValue(typeName, out var valB);

                    long countA = valA.Count;
                    long sizeA = valA.TotalSize;
                    long countB = valB.Count;
                    long sizeB = valB.TotalSize;
                    long deltaBytes = sizeB - sizeA;
                    long deltaCount = countB - countA;

                    // 仅收录产生增量或者存活规模显著的对象
                    if (deltaBytes <= 0 && deltaCount <= 0 && sizeB < 1024 * 1024)
                    {
                        continue;
                    }

                    results.Add(new HeapObjectGrowthItem
                    {
                        TypeName = typeName,
                        CountA = countA,
                        CountB = countB,
                        SizeA = sizeA,
                        SizeB = sizeB,
                        Category = InferTypeCategory(typeName),
                        SuspectRootReason = InferRootReason(typeName, deltaBytes, deltaCount)
                    });
                }

                // 优先按增量字节数降序排序，若增量相同则按 B 转储当前大小排序
                return results.OrderByDescending(r => r.DeltaBytes)
                              .ThenByDescending(r => r.SizeB)
                              .Take(100)
                              .ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ModuleTag, $"托管堆差分分析失败 ({Path.GetFileName(dumpPathA)} vs {Path.GetFileName(dumpPathB)}): {ex.Message}", ex);
                return results;
            }
        }

        private Dictionary<string, (long Count, long TotalSize)> CollectHeapHistogram(string dumpPath)
        {
            var map = new Dictionary<string, (long Count, long TotalSize)>(StringComparer.Ordinal);

            try
            {
                using var dataTarget = DataTarget.LoadDump(dumpPath);
                if (dataTarget.ClrVersions.Length == 0)
                {
                    return map;
                }

                var clrInfo = dataTarget.ClrVersions[0];
                using var runtime = clrInfo.CreateRuntime();
                var heap = runtime.Heap;

                if (!heap.CanWalkHeap)
                {
                    _logger.Warning(ModuleTag, $"转储文件 {Path.GetFileName(dumpPath)} 中的 GC 堆不完整或处于不可遍历状态。");
                    return map;
                }

                foreach (var obj in heap.EnumerateObjects())
                {
                    var type = obj.Type;
                    string name = type?.Name ?? "Free/Unknown";

                    // 忽略空闲内存块
                    if (name.Equals("Free", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    long size = (long)obj.Size;
                    if (map.TryGetValue(name, out var current))
                    {
                        map[name] = (current.Count + 1, current.TotalSize + size);
                    }
                    else
                    {
                        map[name] = (1, size);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ModuleTag, $"读取托管堆对象直方图异常 ({Path.GetFileName(dumpPath)}): {ex.Message}");
            }

            return map;
        }

        private static string InferTypeCategory(string typeName)
        {
            if (typeName.EndsWith("[]", StringComparison.Ordinal) || typeName.Contains("Byte", StringComparison.OrdinalIgnoreCase))
            {
                return "字节数组 / 缓冲区";
            }
            if (typeName.Equals("System.String", StringComparison.Ordinal))
            {
                return "字符串池";
            }
            if (typeName.Contains("Dictionary", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("List<", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("Queue", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("Concurrent", StringComparison.OrdinalIgnoreCase))
            {
                return "集合缓存容器";
            }
            if (typeName.Contains("Action", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("EventHandler", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("Delegate", StringComparison.OrdinalIgnoreCase))
            {
                return "事件 / 委托发布者";
            }
            if (typeName.Contains("Task", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("Async", StringComparison.OrdinalIgnoreCase))
            {
                return "异步任务 / 状态机";
            }
            return "业务数据实体";
        }

        private static string InferRootReason(string typeName, long deltaBytes, long deltaCount)
        {
            if (deltaBytes > 10 * 1024 * 1024)
            {
                if (typeName.Contains("Byte", StringComparison.OrdinalIgnoreCase) || typeName.EndsWith("[]", StringComparison.Ordinal))
                {
                    return "可能存在未受控的图像帧缓冲区、网络报文队列或高速写入的内存流积压。";
                }
                if (typeName.Contains("Dictionary", StringComparison.OrdinalIgnoreCase) || typeName.Contains("List", StringComparison.OrdinalIgnoreCase))
                {
                    return "疑似全局静态字典、单例缓存容器持续 Add 未进行过期淘汰清理。";
                }
                return "大对象堆 (LOH) 或存活代际长期持有，请排查长生命周期对象引用。";
            }
            if (deltaCount > 5000)
            {
                return "微小对象高频创建且未被 GC 及时回收，可能存在事件未注销导致强引用滞留。";
            }
            return "内存增长符合特征，建议结合调用栈追溯引用链。";
        }
    }
}
