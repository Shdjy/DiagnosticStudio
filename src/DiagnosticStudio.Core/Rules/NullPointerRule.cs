using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 空指针解引用分析规则 (Null Pointer Dereference)
    /// </summary>
    public sealed class NullPointerRule : IDiagnosticRule
    {
        public string RuleName => "NullPointerDereference";

        public string Description => "检测由于解引用空指针或空对象结构体成员引发的致命内存访问违例。";

        public IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport)
        {
            if (dumpReport?.Exception == null)
            {
                yield break;
            }

            var exp = dumpReport.Exception;
            if (exp.ExceptionCode != 0xC0000005)
            {
                yield break;
            }

            // 检查目标内存地址是否在 NULL 保护页范围 (通常小于 64KB = 0x10000)
            if (exp.TargetMemoryAddress.HasValue && exp.TargetMemoryAddress.Value < 0x10000)
            {
                var crashThread = dumpReport.Threads.FirstOrDefault(t => t.ThreadId == exp.ThreadId)
                               ?? dumpReport.Threads.FirstOrDefault();

                var rootCauseFrame = crashThread?.Frames.FirstOrDefault(f => f.IsRootCause)
                                  ?? crashThread?.Frames.FirstOrDefault(f => f.IsUserCode)
                                  ?? crashThread?.Frames.FirstOrDefault();

                var triggerFrame = crashThread?.Frames.FirstOrDefault();

                var finding = new DiagnosticFinding
                {
                    Level = DiagnosticLevel.Critical,
                    Title = "🔴 致命空指针解引用 (Potential Null Pointer Dereference)",
                    Description = $"目标程序在试图执行 {exp.AccessType} 操作时崩溃，访问的非法目标内存地址为 0x{exp.TargetMemoryAddress.Value:X16}。该地址位于 Windows 保留空指针保护页内，通常由访问未初始化或已被清空的指针/对象成员引发。",
                    Module = rootCauseFrame?.ModuleName ?? triggerFrame?.ModuleName ?? "Unknown",
                    Function = rootCauseFrame?.FunctionName ?? triggerFrame?.FunctionName ?? "Unknown",
                    SourceFile = rootCauseFrame?.SourceFile ?? triggerFrame?.SourceFile ?? string.Empty,
                    Line = rootCauseFrame?.LineNumber ?? triggerFrame?.LineNumber ?? 0,
                    Confidence = 0.95
                };

                finding.Evidence.Add($"异常代码: 0x{exp.ExceptionCode:X8} ({exp.ExceptionName})");
                finding.Evidence.Add($"崩溃指令地址: 0x{exp.ExceptionAddress:X16}");
                finding.Evidence.Add($"非法访问目标地址: 0x{exp.TargetMemoryAddress.Value:X16} (落在 0x0 ~ 0x10000 空指针页)");
                finding.Evidence.Add($"操作类型: {exp.AccessType}");

                if (triggerFrame != null)
                {
                    finding.Evidence.Add($"系统顶层栈帧: {triggerFrame.ModuleName}!{triggerFrame.FunctionName}");
                }

                if (rootCauseFrame != null && rootCauseFrame != triggerFrame)
                {
                    finding.Evidence.Add($"可疑业务入口: {rootCauseFrame.ModuleName}!{rootCauseFrame.FunctionName}");
                    if (!string.IsNullOrEmpty(rootCauseFrame.SourceFile))
                    {
                        finding.Evidence.Add($"源码位置: {rootCauseFrame.SourceFile} 第 {rootCauseFrame.LineNumber} 行");
                    }
                }

                finding.Recommendations.Add("检查崩溃函数中参与指针运算的入参或类成员变量，确认是否为 nullptr 或 NULL。");
                finding.Recommendations.Add("检查前序步骤是否有工厂创建、查找节点或资源加载失败未做非空判断 (if (ptr == nullptr)) 即直接调用的情况。");
                finding.Recommendations.Add("如果在多线程环境下，排查是否存在对象已被其他线程释放并置空、但当前线程未加锁保护导致的竞态条件。");

                if (crashThread?.Frames != null)
                {
                    finding.RelatedStack.AddRange(crashThread.Frames.Take(10));
                }

                yield return finding;
            }
        }
    }
}
