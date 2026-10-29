using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Reports
{
    /// <summary>
    /// 标准 JSON 诊断报告生成器
    /// 支持完整结构化证据、规则审计轨迹与可选隐私数据脱敏导出
    /// </summary>
    public sealed class JsonReportGenerator : IReportGenerator
    {
        private readonly IReportSanitizer _sanitizer;

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        public JsonReportGenerator(IReportSanitizer? sanitizer = null)
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
                return "{}";
            }

            var modelToExport = sanitizerOptions != null
                ? _sanitizer.SanitizeReport(reportModel, sanitizerOptions)
                : reportModel;

            return JsonSerializer.Serialize(modelToExport, Options);
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

            string json = Generate(reportModel, sanitizerOptions);
            string? dir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(targetFilePath, json, Encoding.UTF8);
        }
    }
}
