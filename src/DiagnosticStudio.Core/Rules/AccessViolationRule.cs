using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 通用非法内存读写与野指针访问规则 (General Access Violation / Wild Pointer)
    /// </summary>
    public sealed class AccessViolationRule : IDiagnosticRule
    {
        public string RuleName => "AccessViolation";

        public string Description => "检测非法地址访问、野指针解引用、缓冲区越界或已释放内存(Use-After-Free)。";

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

            // 如果不是空指针页（大于 0x10000），则属于野指针或越界非法访问
            if (!exp.TargetMemoryAddress.HasValue || exp.TargetMemoryAddress.Value >= 0x10000)
            {
                var crashThread = dumpReport.Threads.FirstOrDefault(t => t.ThreadId == exp.ThreadId)
                               ?? dumpReport.Threads.FirstOrDefault();

                var rootCauseFrame = crashThread?.Frames.FirstOrDefault(f => f.IsRootCause)
                                  ?? crashThread?.Frames.FirstOrDefault(f => f.IsUserCode)
                                  ?? crashThread?.Frames.FirstOrDefault();

                var triggerFrame = crashThread?.Frames.FirstOrDefault();

                ulong targetAddr = exp.TargetMemoryAddress ?? 0;
                string patternAnalysis = AnalyzeAddressPattern(targetAddr);

                var finding = new DiagnosticFinding
                {
                    Level = DiagnosticLevel.Critical,
                    Title = $"🔴 内存访问违例与野指针 (Access Violation - {patternAnalysis})",
                    Description = $"目标程序发生非法内存操作（{exp.AccessType}），目标地址 0x{targetAddr:X16} 不属于进程有效的提交虚拟内存页。{patternAnalysis}",
                    Module = rootCauseFrame?.ModuleName ?? triggerFrame?.ModuleName ?? "Unknown",
                    Function = rootCauseFrame?.FunctionName ?? triggerFrame?.FunctionName ?? "Unknown",
                    SourceFile = rootCauseFrame?.SourceFile ?? triggerFrame?.SourceFile ?? string.Empty,
                    Line = rootCauseFrame?.LineNumber ?? triggerFrame?.LineNumber ?? 0,
                    Confidence = 0.90
                };

                finding.Evidence.Add($"异常代码: 0xC0000005 (STATUS_ACCESS_VIOLATION)");
                finding.Evidence.Add($"崩溃指令: 0x{exp.ExceptionAddress:X16}");
                finding.Evidence.Add($"目标内存: 0x{targetAddr:X16}");
                finding.Evidence.Add($"特征识别: {patternAnalysis}");

                if (triggerFrame != null)
                {
                    finding.Evidence.Add($"系统顶层栈帧: {triggerFrame.ModuleName}!{triggerFrame.FunctionName}");
                }

                if (rootCauseFrame != null && rootCauseFrame != triggerFrame)
                {
                    finding.Evidence.Add($"代码定位: {rootCauseFrame.ModuleName}!{rootCauseFrame.FunctionName}");
                    if (!string.IsNullOrEmpty(rootCauseFrame.SourceFile))
                    {
                        finding.Evidence.Add($"源码文件: {rootCauseFrame.SourceFile} 第 {rootCauseFrame.LineNumber} 行");
                    }
                }

                finding.Recommendations.Add("排查野指针或悬挂指针 (Dangling Pointer)，确认指针所指向的堆内存是否已被提前 free/delete。");
                finding.Recommendations.Add("排查数组、缓冲区越界拷贝（如 memcpy/strcpy/sprintf），确认长度计算是否溢出导致覆盖了指针。");
                finding.Recommendations.Add("建议在调试配置下开启 Pageheap (gflags.exe -i your_app.exe +hpa) 以便在越界或悬挂访问的瞬间立即触发精准定位。");

                if (crashThread?.Frames != null)
                {
                    finding.RelatedStack.AddRange(crashThread.Frames.Take(10));
                }

                yield return finding;
            }
        }

        private static string AnalyzeAddressPattern(ulong address)
        {
            if (address == 0xFEEEFEEEFEEEFEEE || (address & 0xFFFFFFFF) == 0xFEEEFEEE)
            {
                return "特征命中 0xFEEEFEEE（堆已释放标记，疑似 Use-After-Free）";
            }
            if (address == 0xCDCDCDCDCDCDCDCD || (address & 0xFFFFFFFF) == 0xCDCDCDCD)
            {
                return "特征命中 0xCDCDCDCD（CRT 调试堆未初始化内存）";
            }
            if (address == 0xBAADF00DBAADF00D || (address & 0xFFFFFFFF) == 0xBAADF00D)
            {
                return "特征命中 0xBAADF00D（LocalAlloc 分配但未初始化内存）";
            }
            if (address == 0xCCCCCCCCCCCCCCCC || (address & 0xFFFFFFFF) == 0xCCCCCCCC)
            {
                return "特征命中 0xCCCCCCCC（栈上未初始化局部变量）";
            }

            return "常规未映射地址非法访问";
        }
    }
}
