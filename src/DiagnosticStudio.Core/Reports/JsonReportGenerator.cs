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
    /// </summary>
    public sealed class JsonReportGenerator : IReportGenerator
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        public string Generate(DumpReportModel reportModel)
        {
            if (reportModel == null)
            {
                return "{}";
            }

            return JsonSerializer.Serialize(reportModel, Options);
        }

        public void ExportToFile(DumpReportModel reportModel, string targetFilePath)
        {
            if (string.IsNullOrEmpty(targetFilePath))
            {
                throw new ArgumentException("目标输出路径不能为空", nameof(targetFilePath));
            }

            string json = Generate(reportModel);
            string? dir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(targetFilePath, json, Encoding.UTF8);
        }
    }
}
