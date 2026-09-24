using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 栈缓冲区溢出与安全检查保护违例规则 (Stack Buffer Overrun & Security Check Failure)
    /// </summary>
    public sealed class BufferOverflowRule : IDiagnosticRule
    {
        public string RuleName => "BufferOverflow";

        public string Description => "检测栈缓冲区写入越界、栈破坏金丝雀安全检查失败 (__security_check_cookie) 或 __fastfail 致命安全终止。";

        public IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport)
        {
            if (dumpReport?.Exception == null)
            {
                yield break;
            }

            var exp = dumpReport.Exception;
            // 0xC0000409 = STATUS_STACK_BUFFER_OVERRUN
            if (exp.ExceptionCode != 0xC0000409)
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
                Title = "🔴 栈缓冲区破坏与安全保护违例 (Stack Buffer Overrun - 0xC0000409)",
                Description = "目标程序触发了 Windows 编译器栈保护机制 (STATUS_STACK_BUFFER_OVERRUN, 0xC0000409)。通常由向栈上局部数组/缓冲区写入超长数据，踩踏了栈帧金丝雀 (Security Cookie) 或返回地址引起，操作系统已强制防御性终止进程以防代码注入。",
                Module = rootCauseFrame?.ModuleName ?? triggerFrame?.ModuleName ?? "Unknown",
                Function = rootCauseFrame?.FunctionName ?? triggerFrame?.FunctionName ?? "Unknown",
                SourceFile = rootCauseFrame?.SourceFile ?? triggerFrame?.SourceFile ?? string.Empty,
                Line = rootCauseFrame?.LineNumber ?? triggerFrame?.LineNumber ?? 0,
                Confidence = 0.98
            };

            finding.Evidence.Add("异常代码: 0xC0000409 (STATUS_STACK_BUFFER_OVERRUN / FAST_FAIL)");
            finding.Evidence.Add($"崩溃指令地址: 0x{exp.ExceptionAddress:X16}");
            finding.Evidence.Add($"受影响线程: TID {exp.ThreadId}");

            if (triggerFrame != null)
            {
                finding.Evidence.Add($"系统拦截触发点: {triggerFrame.ModuleName}!{triggerFrame.FunctionName}");
            }

            if (rootCauseFrame != null && rootCauseFrame != triggerFrame)
            {
                finding.Evidence.Add($"业务责任代码: {rootCauseFrame.ModuleName}!{rootCauseFrame.FunctionName}");
                if (!string.IsNullOrEmpty(rootCauseFrame.SourceFile))
                {
                    finding.Evidence.Add($"源码位置: {rootCauseFrame.SourceFile} 第 {rootCauseFrame.LineNumber} 行");
                }
            }

            finding.Recommendations.Add("排查栈上局部变量数组（如 char buf[N]），检查 memset、memcpy、strcpy、sprintf 等内存复制操作是否越界。");
            finding.Recommendations.Add("将固定大小的原始字符数组改用 std::string、std::vector 或安全函数（如 strcpy_s、memcpy_s）。");
            finding.Recommendations.Add("建议在编译选项中开启 AddressSanitizer (/fsanitize=address)，以便在发生越界踩踏的瞬间准确定位首个非法写操作。");

            if (crashThread?.Frames != null)
            {
                finding.RelatedStack.AddRange(crashThread.Frames.Take(12));
            }

            yield return finding;
        }
    }
}
