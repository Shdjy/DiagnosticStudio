using System;
using System.IO;
using System.Text.RegularExpressions;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Reports
{
    /// <summary>
    /// 标准诊断报告脱敏处理器实现
    /// </summary>
    public sealed class ReportSanitizer : IReportSanitizer
    {
        private static readonly Regex UserPathRegex = new Regex(
            @"(?i)(users[\\/])([^\\/]+)",
            RegexOptions.Compiled);

        private static readonly Regex IpRegex = new Regex(
            @"\b(?:\d{1,3}\.){3}\d{1,3}\b",
            RegexOptions.Compiled);

        public string SanitizeText(string text, ReportSanitizerOptions? options = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            options ??= new ReportSanitizerOptions();
            string result = text;

            if (options.SanitizeUserNames)
            {
                result = UserPathRegex.Replace(result, "$1<USER>");
            }

            if (options.SanitizeIpAddresses)
            {
                result = IpRegex.Replace(result, "<IP_REDACTED>");
            }

            return result;
        }

        public DumpReportModel SanitizeReport(DumpReportModel original, ReportSanitizerOptions? options = null)
        {
            if (original == null)
            {
                throw new ArgumentNullException(nameof(original));
            }

            options ??= new ReportSanitizerOptions();

            // 浅克隆外层对象，脱敏关键文本字段
            var sanitized = new DumpReportModel
            {
                DumpPath = SanitizeText(original.DumpPath, options),
                FileSizeBytes = original.FileSizeBytes,
                AnalysisTime = original.AnalysisTime,
                AnalyzerVersion = original.AnalyzerVersion,
                RuleEngineVersion = original.RuleEngineVersion,
                OverallStatus = original.OverallStatus,
                Integrity = original.Integrity,
                SystemInfo = original.SystemInfo,
                IsRecovered = original.IsRecovered
            };

            if (original.Exception != null)
            {
                sanitized.Exception = new ExceptionInfo
                {
                    ExceptionCode = original.Exception.ExceptionCode,
                    ExceptionFlags = original.Exception.ExceptionFlags,
                    ExceptionAddress = original.Exception.ExceptionAddress,
                    ThreadId = original.Exception.ThreadId
                };
                sanitized.Exception.Parameters.AddRange(original.Exception.Parameters);
            }

            // 模块脱敏
            foreach (var mod in original.Modules)
            {
                sanitized.Modules.Add(new ModuleInfo
                {
                    BaseOfImage = mod.BaseOfImage,
                    SizeOfImage = mod.SizeOfImage,
                    ModuleName = mod.ModuleName,
                    ImageName = SanitizeText(mod.ImageName, options),
                    LoadedPdbName = SanitizeText(mod.LoadedPdbName, options),
                    IsSymbolLoaded = mod.IsSymbolLoaded,
                    SymbolQuality = mod.SymbolQuality
                });
            }

            // 线程与栈帧脱敏
            foreach (var thread in original.Threads)
            {
                var sThread = new ThreadInfo
                {
                    Index = thread.Index,
                    ThreadId = thread.ThreadId,
                    IsCrashThread = thread.IsCrashThread
                };

                foreach (var f in thread.Frames)
                {
                    string src = f.SourceFile;
                    if (options.SanitizeSourcePaths && !string.IsNullOrEmpty(src))
                    {
                        src = SanitizeText(src, options);
                    }

                    sThread.Frames.Add(new StackFrameInfo
                    {
                        InstructionOffset = f.InstructionOffset,
                        ReturnOffset = f.ReturnOffset,
                        FrameOffset = f.FrameOffset,
                        StackOffset = f.StackOffset,
                        ModuleName = f.ModuleName,
                        FunctionName = f.FunctionName,
                        SourceFile = src,
                        LineNumber = f.LineNumber,
                        Displacement = f.Displacement,
                        IsUserCode = f.IsUserCode,
                        IsRootCause = f.IsRootCause,
                        HighlightLevel = f.HighlightLevel
                    });
                }

                sanitized.Threads.Add(sThread);
            }

            // 诊断问题项脱敏
            foreach (var f in original.Findings)
            {
                var sFinding = new DiagnosticFinding
                {
                    FindingId = f.FindingId,
                    RuleId = f.RuleId,
                    RuleVersion = f.RuleVersion,
                    Level = f.Level,
                    Title = SanitizeText(f.Title, options),
                    Description = SanitizeText(f.Description, options),
                    Module = f.Module,
                    Function = f.Function,
                    SourceFile = SanitizeText(f.SourceFile, options),
                    Line = f.Line,
                    Confidence = f.Confidence,
                    LocationRole = f.LocationRole
                };

                foreach (var ev in f.Evidence)
                {
                    sFinding.Evidence.Add(SanitizeText(ev, options));
                }
                foreach (var rec in f.Recommendations)
                {
                    sFinding.Recommendations.Add(SanitizeText(rec, options));
                }
                foreach (var fact in f.ConfirmedFacts)
                {
                    sFinding.ConfirmedFacts.Add(SanitizeText(fact, options));
                }
                foreach (var hyp in f.Hypotheses)
                {
                    sFinding.Hypotheses.Add(SanitizeText(hyp, options));
                }
                foreach (var step in f.VerificationSteps)
                {
                    sFinding.VerificationSteps.Add(SanitizeText(step, options));
                }
                foreach (var sev in f.StructuredEvidences)
                {
                    sFinding.StructuredEvidences.Add(new EvidenceItem
                    {
                        Category = sev.Category,
                        Description = SanitizeText(sev.Description, options),
                        Weight = sev.Weight,
                        RelatedAddress = sev.RelatedAddress,
                        RelatedModule = sev.RelatedModule,
                        RelatedThreadId = sev.RelatedThreadId
                    });
                }

                sanitized.Findings.Add(sFinding);
            }

            sanitized.RuleExecutions.AddRange(original.RuleExecutions);

            return sanitized;
        }
    }
}
