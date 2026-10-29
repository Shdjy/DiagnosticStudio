using System;
using System.IO;
using System.Linq;
using System.Text;
using DiagnosticStudio.Core.Models;
using DiagnosticStudio.Core.Rules;

namespace DiagnosticStudio.Core.Reports
{
    /// <summary>
    /// 专业自包含 HTML 诊断报告生成器
    /// 具备 Dump 完整性自检展示、规则执行审计追踪、多维结构化证据呈现与脱敏支持
    /// </summary>
    public sealed class HtmlReportGenerator : IReportGenerator
    {
        private readonly IReportSanitizer _sanitizer;

        public HtmlReportGenerator(IReportSanitizer? sanitizer = null)
        {
            _sanitizer = sanitizer ?? new ReportSanitizer();
        }

        public string Generate(DumpReportModel reportModel)
        {
            return Generate(reportModel, sanitizerOptions: null);
        }

        public string Generate(DumpReportModel reportModel, ReportSanitizerOptions? sanitizerOptions)
        {
            if (reportModel == null)
            {
                return "<html><body><h1>Empty Report</h1></body></html>";
            }

            var model = sanitizerOptions != null
                ? _sanitizer.SanitizeReport(reportModel, sanitizerOptions)
                : reportModel;

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"zh-CN\">");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"UTF-8\" />");
            sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
            sb.AppendLine($"  <title>Diagnostic Studio 故障诊断报告 - {Path.GetFileName(model.DumpPath)}</title>");
            sb.AppendLine("  <style>");
            sb.AppendLine("    :root { --bg: #1e1e24; --card-bg: #2b2b36; --border: #3d3d4e; --text: #e2e8f0; --accent: #38bdf8; --critical: #ef4444; --high: #f97316; --warning: #eab308; --info: #3b82f6; --success: #22c55e; }");
            sb.AppendLine("    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; background-color: var(--bg); color: var(--text); margin: 0; padding: 24px; line-height: 1.6; }");
            sb.AppendLine("    .container { max-width: 1200px; margin: 0 auto; }");
            sb.AppendLine("    .header { padding-bottom: 20px; border-bottom: 1px solid var(--border); margin-bottom: 24px; }");
            sb.AppendLine("    .header h1 { margin: 0 0 8px 0; color: #fff; font-size: 26px; }");
            sb.AppendLine("    .badge { display: inline-block; padding: 4px 10px; border-radius: 6px; font-weight: 600; font-size: 13px; margin-right: 8px; }");
            sb.AppendLine("    .badge-critical { background-color: rgba(239, 68, 68, 0.2); color: var(--critical); border: 1px solid var(--critical); }");
            sb.AppendLine("    .badge-high { background-color: rgba(249, 115, 22, 0.2); color: var(--high); border: 1px solid var(--high); }");
            sb.AppendLine("    .badge-warning { background-color: rgba(234, 179, 8, 0.2); color: var(--warning); border: 1px solid var(--warning); }");
            sb.AppendLine("    .badge-info { background-color: rgba(59, 130, 246, 0.2); color: var(--info); border: 1px solid var(--info); }");
            sb.AppendLine("    .badge-success { background-color: rgba(34, 197, 94, 0.2); color: var(--success); border: 1px solid var(--success); }");
            sb.AppendLine("    .card { background-color: var(--card-bg); border: 1px solid var(--border); border-radius: 8px; padding: 20px; margin-bottom: 24px; }");
            sb.AppendLine("    .card-title { font-size: 18px; font-weight: 600; margin-top: 0; margin-bottom: 16px; color: #fff; border-left: 4px solid var(--accent); padding-left: 10px; }");
            sb.AppendLine("    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 16px; margin-bottom: 20px; }");
            sb.AppendLine("    .metric-box { background: rgba(0,0,0,0.2); padding: 14px; border-radius: 6px; border: 1px solid var(--border); }");
            sb.AppendLine("    .metric-label { font-size: 12px; color: #94a3b8; text-transform: uppercase; }");
            sb.AppendLine("    .metric-value { font-size: 20px; font-weight: 700; color: #fff; margin-top: 4px; }");
            sb.AppendLine("    .finding-card { border-left: 5px solid var(--critical); padding: 16px; background: rgba(0,0,0,0.25); border-radius: 4px; margin-bottom: 16px; }");
            sb.AppendLine("    .finding-card.warning { border-left-color: var(--warning); }");
            sb.AppendLine("    .finding-card.high { border-left-color: var(--high); }");
            sb.AppendLine("    .finding-title { font-size: 16px; font-weight: 700; color: #fff; margin-bottom: 8px; }");
            sb.AppendLine("    table { width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 13px; }");
            sb.AppendLine("    th, td { text-align: left; padding: 10px 12px; border-bottom: 1px solid var(--border); }");
            sb.AppendLine("    th { background: rgba(0,0,0,0.3); color: #94a3b8; }");
            sb.AppendLine("    tr.row-critical { background-color: rgba(239, 68, 68, 0.15); font-weight: 600; }");
            sb.AppendLine("    tr.row-warning { background-color: rgba(234, 179, 8, 0.12); }");
            sb.AppendLine("    code { background: rgba(0,0,0,0.3); padding: 2px 6px; border-radius: 4px; font-family: Consolas, monospace; color: #38bdf8; }");
            sb.AppendLine("    ul { margin: 6px 0; padding-left: 20px; }");
            sb.AppendLine("    .section-title { font-weight: 600; color: #cbd5e1; margin-top: 10px; }");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<div class=\"container\">");

            // Header
            sb.AppendLine("  <div class=\"header\">");
            sb.AppendLine("    <h1>⚡ Diagnostic Studio 故障诊断分析报告</h1>");
            sb.AppendLine($"    <p style=\"color: #94a3b8; margin: 0;\">转储文件: <code>{model.DumpPath}</code> | 分析生成时间: {model.AnalysisTime:yyyy-MM-dd HH:mm:ss} | 分析引擎: v{model.AnalyzerVersion} (规则集: v{model.RuleEngineVersion})</p>");
            sb.AppendLine("  </div>");

            // 统计看板与完整性
            int criticalCount = model.Findings.Count(f => f.Level == DiagnosticLevel.Critical);
            int highCount = model.Findings.Count(f => f.Level == DiagnosticLevel.High);
            int warningCount = model.Findings.Count(f => f.Level == DiagnosticLevel.Warning);
            int infoCount = model.Findings.Count(f => f.Level == DiagnosticLevel.Info);

            sb.AppendLine("  <div class=\"grid\">");
            sb.AppendLine($"    <div class=\"metric-box\"><div class=\"metric-label\">物理完整性</div><div class=\"metric-value\" style=\"font-size: 16px;\">{model.Integrity.StatusBadge}</div></div>");
            sb.AppendLine($"    <div class=\"metric-box\"><div class=\"metric-label\">致命风险 (Critical)</div><div class=\"metric-value\" style=\"color: var(--critical);\">{criticalCount} 项</div></div>");
            sb.AppendLine($"    <div class=\"metric-box\"><div class=\"metric-label\">高风险问题 (High)</div><div class=\"metric-value\" style=\"color: var(--high);\">{highCount} 项</div></div>");
            sb.AppendLine($"    <div class=\"metric-box\"><div class=\"metric-label\">可疑预警 (Warning)</div><div class=\"metric-value\" style=\"color: var(--warning);\">{warningCount} 项</div></div>");
            sb.AppendLine("  </div>");

            // 异常详情卡片
            if (model.Exception != null)
            {
                var exp = model.Exception;
                sb.AppendLine("  <div class=\"card\">");
                sb.AppendLine("    <div class=\"card-title\">💥 崩溃异常摘要 (Exception Summary)</div>");
                sb.AppendLine("    <div class=\"grid\">");
                sb.AppendLine($"      <div class=\"metric-box\"><div class=\"metric-label\">异常代码</div><div class=\"metric-value\" style=\"color: var(--critical);\">0x{exp.ExceptionCode:X8}</div><div style=\"font-size: 12px; color: #94a3b8;\">{exp.ExceptionName}</div></div>");
                sb.AppendLine($"      <div class=\"metric-box\"><div class=\"metric-label\">崩溃指令地址</div><div class=\"metric-value\">0x{exp.ExceptionAddress:X16}</div></div>");
                sb.AppendLine($"      <div class=\"metric-box\"><div class=\"metric-label\">故障线程 ID</div><div class=\"metric-value\">TID {exp.ThreadId}</div></div>");
                if (exp.TargetMemoryAddress.HasValue)
                {
                    sb.AppendLine($"      <div class=\"metric-box\"><div class=\"metric-label\">目标违规地址 / 类型</div><div class=\"metric-value\" style=\"color: var(--critical);\">0x{exp.TargetMemoryAddress.Value:X16}</div><div style=\"font-size: 12px; color: #94a3b8;\">{exp.AccessType}</div></div>");
                }
                sb.AppendLine("    </div>");
                sb.AppendLine("  </div>");
            }

            // 智能诊断 Findings
            sb.AppendLine("  <div class=\"card\">");
            sb.AppendLine("    <div class=\"card-title\">🔍 诊断推导与结构化证据 (Diagnostic Findings & Evidence Chain)</div>");

            if (model.Findings.Count == 0)
            {
                sb.AppendLine("    <p style=\"color: #94a3b8;\">未检测到明确的规则违规项。</p>");
            }
            else
            {
                foreach (var finding in model.Findings)
                {
                    string cardClass = finding.Level switch
                    {
                        DiagnosticLevel.Warning => "finding-card warning",
                        DiagnosticLevel.High => "finding-card high",
                        _ => "finding-card"
                    };

                    sb.AppendLine($"    <div class=\"{cardClass}\">");
                    sb.AppendLine($"      <div class=\"finding-title\">{finding.LevelBadge} - {finding.Title} (置信度: {(finding.Confidence * 100):F0}%)</div>");
                    sb.AppendLine($"      <p>{finding.Description}</p>");

                    if (!string.IsNullOrEmpty(finding.SourceFile) && finding.Line > 0)
                    {
                        sb.AppendLine($"      <p><strong>📍 可疑代码位置:</strong> <code>{finding.SourceFile}:{finding.Line}</code> ({finding.Module}!{finding.Function})</p>");
                    }

                    if (finding.ConfirmedFacts.Count > 0)
                    {
                        sb.AppendLine("      <div class=\"section-title\">✅ 已确认为真实的底层事实 (Confirmed Facts):</div><ul>");
                        foreach (var fact in finding.ConfirmedFacts)
                        {
                            sb.AppendLine($"        <li>{fact}</li>");
                        }
                        sb.AppendLine("      </ul>");
                    }

                    if (finding.Hypotheses.Count > 0)
                    {
                        sb.AppendLine("      <div class=\"section-title\">💡 诊断假设与推测 (Hypotheses):</div><ul>");
                        foreach (var hyp in finding.Hypotheses)
                        {
                            sb.AppendLine($"        <li>{hyp}</li>");
                        }
                        sb.AppendLine("      </ul>");
                    }

                    if (finding.VerificationSteps.Count > 0)
                    {
                        sb.AppendLine("      <div class=\"section-title\">🧪 建议进行的复现与验证步骤 (Verification Steps):</div><ul>");
                        foreach (var step in finding.VerificationSteps)
                        {
                            sb.AppendLine($"        <li>{step}</li>");
                        }
                        sb.AppendLine("      </ul>");
                    }

                    if (finding.Recommendations.Count > 0)
                    {
                        sb.AppendLine("      <div class=\"section-title\">🛠️ 现场排查与修复建议 (Recommendations):</div><ul>");
                        foreach (var rec in finding.Recommendations)
                        {
                            sb.AppendLine($"        <li>{rec}</li>");
                        }
                        sb.AppendLine("      </ul>");
                    }

                    sb.AppendLine("    </div>");
                }
            }
            sb.AppendLine("  </div>");

            // 规则审计记录
            if (model.RuleExecutions.Count > 0)
            {
                sb.AppendLine("  <div class=\"card\">");
                sb.AppendLine($"    <div class=\"card-title\">📋 规则执行审计轨迹 (Rule Execution Audit - 共 {model.RuleExecutions.Count} 条规则)</div>");
                sb.AppendLine("    <table>");
                sb.AppendLine("      <thead><tr><th>规则编号</th><th>规则名称</th><th>版本</th><th>执行状态</th><th>耗时 (ms)</th><th>产出条目</th><th>异常/跳过说明</th></tr></thead>");
                sb.AppendLine("      <tbody>");
                foreach (var rx in model.RuleExecutions)
                {
                    string statusBadge = rx.Status switch
                    {
                        RuleExecutionStatus.Success => "<span style=\"color: var(--success); font-weight:600;\">✓ 成功</span>",
                        RuleExecutionStatus.Failed => "<span style=\"color: var(--critical); font-weight:600;\">❌ 失败</span>",
                        _ => "<span style=\"color: #94a3b8;\">已跳过</span>"
                    };

                    string note = rx.Status == RuleExecutionStatus.Failed ? $"{rx.ExceptionType}: {rx.ErrorMessage}" : (rx.SkipReason ?? "-");
                    sb.AppendLine("        <tr>");
                    sb.AppendLine($"          <td><code>{rx.RuleId}</code></td>");
                    sb.AppendLine($"          <td><strong>{rx.RuleName}</strong></td>");
                    sb.AppendLine($"          <td>{rx.Version}</td>");
                    sb.AppendLine($"          <td>{statusBadge}</td>");
                    sb.AppendLine($"          <td>{rx.ElapsedMilliseconds:F2}</td>");
                    sb.AppendLine($"          <td>{rx.FindingsCount}</td>");
                    sb.AppendLine($"          <td style=\"color: {(rx.Status == RuleExecutionStatus.Failed ? "var(--critical)" : "#94a3b8")};\">{note}</td>");
                    sb.AppendLine("        </tr>");
                }
                sb.AppendLine("      </tbody>");
                sb.AppendLine("    </table>");
                sb.AppendLine("  </div>");
            }

            // 崩溃线程调用栈
            var crashThread = model.Threads.FirstOrDefault(t => t.IsCrashThread) ?? model.Threads.FirstOrDefault();
            if (crashThread != null)
            {
                sb.AppendLine("  <div class=\"card\">");
                sb.AppendLine($"    <div class=\"card-title\">📜 崩溃线程调用栈 (Thread TID {crashThread.ThreadId})</div>");
                sb.AppendLine("    <table>");
                sb.AppendLine("      <thead><tr><th>#</th><th>模块</th><th>函数</th><th>源码文件</th><th>行号</th><th>指令偏移</th></tr></thead>");
                sb.AppendLine("      <tbody>");

                for (int i = 0; i < crashThread.Frames.Count; i++)
                {
                    var f = crashThread.Frames[i];
                    string rowClass = f.HighlightLevel switch
                    {
                        DiagnosticLevel.Critical => " class=\"row-critical\"",
                        DiagnosticLevel.High or DiagnosticLevel.Warning => " class=\"row-warning\"",
                        _ => ""
                    };

                    sb.AppendLine($"        <tr{rowClass}>");
                    sb.AppendLine($"          <td>{i}</td>");
                    sb.AppendLine($"          <td><code>{f.ModuleName}</code></td>");
                    sb.AppendLine($"          <td><strong>{f.FunctionName}</strong></td>");
                    sb.AppendLine($"          <td>{Path.GetFileName(f.SourceFile)}</td>");
                    sb.AppendLine($"          <td>{(f.LineNumber > 0 ? f.LineNumber.ToString() : "-")}</td>");
                    sb.AppendLine($"          <td>0x{f.InstructionOffset:X16}</td>");
                    sb.AppendLine("        </tr>");
                }

                sb.AppendLine("      </tbody>");
                sb.AppendLine("    </table>");
                sb.AppendLine("  </div>");
            }

            sb.AppendLine("</div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        public void ExportToFile(DumpReportModel reportModel, string targetFilePath)
        {
            ExportToFile(reportModel, targetFilePath, sanitizerOptions: null);
        }

        public void ExportToFile(DumpReportModel reportModel, string targetFilePath, ReportSanitizerOptions? sanitizerOptions)
        {
            if (string.IsNullOrEmpty(targetFilePath))
            {
                throw new ArgumentException("目标输出路径不能为空", nameof(targetFilePath));
            }

            string html = Generate(reportModel, sanitizerOptions);
            string? dir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(targetFilePath, html, Encoding.UTF8);
        }
    }
}
