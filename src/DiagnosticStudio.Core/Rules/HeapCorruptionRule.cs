using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 堆内存损坏与管理结构损毁分析规则 (Heap Corruption - 0xC0000374)
    /// </summary>
    public sealed class HeapCorruptionRule : IDiagnosticRule
    {
        public string RuleName => "HeapCorruption";

        public string Description => "检测堆头踩踏、重复释放 (Double Free) 或向已释放堆块写入引发的堆管理器元数据损坏。";

        public IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport)
        {
            if (dumpReport?.Exception == null)
            {
                yield break;
            }

            var exp = dumpReport.Exception;
            // 0xC0000374 = STATUS_HEAP_CORRUPTION
            if (exp.ExceptionCode != 0xC0000374)
            {
                yield break;
            }

            var crashThread = dumpReport.Threads.FirstOrDefault(t => t.ThreadId == exp.ThreadId)
                           ?? dumpReport.Threads.FirstOrDefault();

            var rootCauseFrame = crashThread?.Frames.FirstOrDefault(f => f.IsRootCause)
                              ?? crashThread?.Frames.FirstOrDefault(f => f.IsUserCode)
                              ?? crashThread?.Frames.FirstOrDefault();

            var triggerFrame = crashThread?.Frames.FirstOrDefault();

            var finding = new DiagnosticFinding
            {
                Level = DiagnosticLevel.Critical,
                Title = "🔴 堆内存损坏与元数据破坏 (Heap Corruption - 0xC0000374)",
                Description = "Windows 底层堆管理器 (NT Heap) 在执行堆分配 (HeapAlloc/malloc/new) 或堆释放 (HeapFree/free/delete) 时，校验发现堆块头部元数据被篡改破坏，为防止任意代码执行，操作系统强行终止了程序。",
                Module = rootCauseFrame?.ModuleName ?? triggerFrame?.ModuleName ?? "ntdll.dll",
                Function = rootCauseFrame?.FunctionName ?? triggerFrame?.FunctionName ?? "RtlReportCriticalFailure",
                SourceFile = rootCauseFrame?.SourceFile ?? triggerFrame?.SourceFile ?? string.Empty,
                Line = rootCauseFrame?.LineNumber ?? triggerFrame?.LineNumber ?? 0,
                Confidence = 0.98
            };

            finding.Evidence.Add("异常代码: 0xC0000374 (STATUS_HEAP_CORRUPTION)");
            finding.Evidence.Add($"崩溃指令地址: 0x{exp.ExceptionAddress:X16}");
            finding.Evidence.Add($"受影响线程: TID {exp.ThreadId}");

            if (triggerFrame != null)
            {
                finding.Evidence.Add($"堆校验拦截点: {triggerFrame.ModuleName}!{triggerFrame.FunctionName}");
            }

            if (rootCauseFrame != null && rootCauseFrame != triggerFrame)
            {
                finding.Evidence.Add($"业务责任代码: {rootCauseFrame.ModuleName}!{rootCauseFrame.FunctionName}");
                if (!string.IsNullOrEmpty(rootCauseFrame.SourceFile))
                {
                    finding.Evidence.Add($"源码位置: {rootCauseFrame.SourceFile} 第 {rootCauseFrame.LineNumber} 行");
                }
            }

            finding.Recommendations.Add("排查是否存在对同一指针执行了两次 delete 或 free 的重复释放 (Double Free) 问题。");
            finding.Recommendations.Add("排查堆内存越界写入踩踏后方相邻堆块头部的行为（如写入超过了 malloc 分配的字节数）。");
            finding.Recommendations.Add("强烈建议开启页堆调试保护：运行命令行 gflags.exe -p /enable your_app.exe /full，精准在越界发生的指令瞬间捕获崩溃。");

            if (crashThread?.Frames != null)
            {
                finding.RelatedStack.AddRange(crashThread.Frames.Take(12));
            }

            yield return finding;
        }
    }
}
