using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Dump.Services
{
    /// <summary>
    /// 可疑代码智能定位与风险标记器实现
    /// </summary>
    public sealed class SuspectCodeLocator : ISuspectCodeLocator
    {
        // 常见系统底层库完整黑名单（支持带扩展名与不带扩展名双向匹配）
        private static readonly HashSet<string> SystemModuleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Windows 核心内核与加载器
            "ntdll", "ntdll.dll", "kernel32", "kernel32.dll", "kernelbase", "kernelbase.dll", "kernel.appcore", "kernel.appcore.dll",
            "wow64", "wow64.dll", "wow64cpu", "wow64cpu.dll", "wow64win", "wow64win.dll",

            // C/C++ 运行时与标准库
            "ucrtbase", "ucrtbase.dll", "msvcrt", "msvcrt.dll", "vcruntime140", "vcruntime140.dll", "vcruntime140d", "vcruntime140d.dll",
            "vcruntime140_1", "vcruntime140_1.dll", "vcruntime140_1d", "vcruntime140_1d.dll",
            "msvcp140", "msvcp140.dll", "msvcp140d", "msvcp140d.dll", "msvcp140_1", "msvcp140_1.dll", "msvcp140_2", "msvcp140_2.dll",
            "msvcp140_atomic_wait", "msvcp140_atomic_wait.dll", "msvcp140_codecvt_ids", "msvcp140_codecvt_ids.dll",
            "vcomp140", "vcomp140.dll", "vcomp140d", "vcomp140d.dll", "concrt140", "concrt140.dll",

            // .NET 运行时与核心组件
            "clr", "clr.dll", "coreclr", "coreclr.dll", "clrjit", "clrjit.dll", "mscorlib", "mscorlib.dll",
            "system.private.corelib", "system.private.corelib.dll", "hostfxr", "hostfxr.dll", "hostpolicy", "hostpolicy.dll",

            // COM、RPC 与安全认证
            "combase", "combase.dll", "ole32", "ole32.dll", "oleaut32", "oleaut32.dll", "rpcrt4", "rpcrt4.dll",
            "advapi32", "advapi32.dll", "sechost", "sechost.dll", "sspicli", "sspicli.dll", "crypt32", "crypt32.dll", "bcrypt", "bcrypt.dll",

            // GUI、输入法与系统外观
            "user32", "user32.dll", "gdi32", "gdi32.dll", "gdi32full", "gdi32full.dll", "gdiplus", "gdiplus.dll",
            "shell32", "shell32.dll", "shlwapi", "shlwapi.dll", "shcore", "shcore.dll", "imm32", "imm32.dll", "uxtheme", "uxtheme.dll", "dwmapi", "dwmapi.dll",

            // 网络通信与诊断
            "ws2_32", "ws2_32.dll", "mswsock", "mswsock.dll", "wininet", "wininet.dll", "winhttp", "winhttp.dll",
            "dbghelp", "dbghelp.dll", "dbgcore", "dbgcore.dll",

            // 图形渲染底层组件
            "d3d9", "d3d9.dll", "d3d11", "d3d11.dll", "d3d12", "d3d12.dll", "dxgi", "dxgi.dll", "opengl32", "opengl32.dll"
        };

        public bool IsUserModule(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                return false;
            }

            string cleanName = Path.GetFileName(moduleName).ToLowerInvariant();
            string nameWithoutExt = Path.GetFileNameWithoutExtension(cleanName);

            if (SystemModuleNames.Contains(cleanName) || SystemModuleNames.Contains(nameWithoutExt))
            {
                return false;
            }

            // Windows 虚拟微系统前缀排除
            if (cleanName.StartsWith("api-ms-win") || cleanName.StartsWith("ext-ms-win") ||
                nameWithoutExt.StartsWith("api-ms-win") || nameWithoutExt.StartsWith("ext-ms-win"))
            {
                return false;
            }

            // 运行时通配排除
            if (nameWithoutExt.StartsWith("msvcr") || nameWithoutExt.StartsWith("msvcp") ||
                nameWithoutExt.StartsWith("vcruntime") || nameWithoutExt.StartsWith("ucrt"))
            {
                return false;
            }

            return true;
        }

        public StackFrameInfo? FindBlamedUserFrame(ThreadInfo? crashThread)
        {
            if (crashThread?.Frames == null || crashThread.Frames.Count == 0)
            {
                return null;
            }

            // 1. 如果调用栈中存在已被标记为 IsRootCause 的帧，直接返回
            var existingRootCause = crashThread.Frames.FirstOrDefault(f => f.IsRootCause);
            if (existingRootCause != null)
            {
                return existingRootCause;
            }

            // 2. 否则查找调用栈从顶至底第一层具备用户业务属性的栈帧
            return crashThread.Frames.FirstOrDefault(f => f.IsUserCode);
        }

        public void MarkSuspectFrames(List<ThreadInfo> threads, uint? crashThreadId)
        {
            if (threads == null || threads.Count == 0)
            {
                return;
            }

            foreach (var thread in threads)
            {
                bool isCrashThread = crashThreadId.HasValue && thread.ThreadId == crashThreadId.Value;
                thread.IsCrashThread = isCrashThread;

                if (thread.Frames == null || thread.Frames.Count == 0)
                {
                    continue;
                }

                // 1. 先对所有帧判定并标注 IsUserCode
                for (int i = 0; i < thread.Frames.Count; i++)
                {
                    var frame = thread.Frames[i];
                    frame.IsUserCode = IsUserModule(frame.ModuleName);
                    frame.IsRootCause = false;
                }

                if (isCrashThread)
                {
                    var topFrame = thread.Frames[0];

                    if (topFrame.IsUserCode)
                    {
                        // 场景 A：崩溃直接在用户业务代码中发生 (如在用户函数中解引用空指针)
                        topFrame.HighlightLevel = DiagnosticLevel.Critical;
                        topFrame.IsRootCause = true;

                        // 上方调用链中若还有更外层的用户业务函数，将第一层外层函数标注为 🟡 High (业务入口)
                        bool foundSubsequentUserFrame = false;
                        for (int i = 1; i < thread.Frames.Count; i++)
                        {
                            var frame = thread.Frames[i];
                            if (frame.IsUserCode && !foundSubsequentUserFrame)
                            {
                                frame.HighlightLevel = DiagnosticLevel.High;
                                foundSubsequentUserFrame = true;
                            }
                        }
                    }
                    else
                    {
                        // 场景 B：崩溃在系统底层库内部爆发 (如 ucrtbase!abort, ntdll!RtlReportCriticalFailure, kernelbase!RaiseException)
                        // 此时系统顶帧仅为告警触发点
                        topFrame.HighlightLevel = DiagnosticLevel.Warning;

                        // 核心反向归咎：沿着调用栈向上寻找第一层调用该系统库的用户业务代码帧！
                        var blamedUserFrame = thread.Frames.FirstOrDefault(f => f.IsUserCode);
                        if (blamedUserFrame != null)
                        {
                            blamedUserFrame.HighlightLevel = DiagnosticLevel.Critical;
                            blamedUserFrame.IsRootCause = true;

                            // 进一步向上寻找更外层的用户入口逻辑标黄
                            bool passedBlamedFrame = false;
                            for (int i = 0; i < thread.Frames.Count; i++)
                            {
                                var frame = thread.Frames[i];
                                if (frame == blamedUserFrame)
                                {
                                    passedBlamedFrame = true;
                                    continue;
                                }

                                if (passedBlamedFrame && frame.IsUserCode)
                                {
                                    frame.HighlightLevel = DiagnosticLevel.High;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // 若整个调用栈完全没有用户代码，才保留第 0 帧为 Critical
                            topFrame.HighlightLevel = DiagnosticLevel.Critical;
                            topFrame.IsRootCause = true;
                        }
                    }
                }
                else
                {
                    // 非崩溃线程中的用户代码行默认为 Info
                    foreach (var frame in thread.Frames)
                    {
                        if (frame.IsUserCode)
                        {
                            frame.HighlightLevel = DiagnosticLevel.Info;
                        }
                    }
                }
            }
        }
    }
}
