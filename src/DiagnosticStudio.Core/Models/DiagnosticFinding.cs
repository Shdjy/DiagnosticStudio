using System.Collections.Generic;

namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 诊断结论统一数据模型
    /// </summary>
    public sealed class DiagnosticFinding
    {
        /// <summary>
        /// 严重度级别 (Critical 🔴 / High 🟠 / Warning 🟡 / Info 🔵)
        /// </summary>
        public DiagnosticLevel Level { get; set; } = DiagnosticLevel.Info;

        /// <summary>
        /// 诊断标题 (如 Potential Null Pointer Dereference)
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 详细现象与原因剖析
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 触发异常的可疑模块
        /// </summary>
        public string Module { get; set; } = string.Empty;

        /// <summary>
        /// 触发异常的可疑函数
        /// </summary>
        public string Function { get; set; } = string.Empty;

        /// <summary>
        /// 关联源代码绝对路径
        /// </summary>
        public string SourceFile { get; set; } = string.Empty;

        /// <summary>
        /// 关联源码行号
        /// </summary>
        public uint Line { get; set; }

        /// <summary>
        /// 规则置信度 (0.0 ~ 1.0)
        /// </summary>
        public double Confidence { get; set; } = 1.0;

        /// <summary>
        /// 底层诊断证据链条 (内存地址、寄存器状态、指令快照)
        /// </summary>
        public List<string> Evidence { get; set; } = new List<string>();

        /// <summary>
        /// 现场排查与修复建议
        /// </summary>
        public List<string> Recommendations { get; set; } = new List<string>();

        /// <summary>
        /// 关联的问题调用栈
        /// </summary>
        public List<StackFrameInfo> RelatedStack { get; set; } = new List<StackFrameInfo>();

        /// <summary>
        /// 格式化级别 Emoji 标记
        /// </summary>
        public string LevelBadge => Level switch
        {
            DiagnosticLevel.Critical => "🔴 Critical",
            DiagnosticLevel.High => "🟠 High",
            DiagnosticLevel.Warning => "🟡 Warning",
            _ => "🔵 Info"
        };

        /// <summary>
        /// 格式化源码定位显示文本 (若无源码则显示函数，具备安全空 setter 兼容 WPF 绑定)
        /// </summary>
        public string CodeLocationDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(SourceFile))
                {
                    return string.IsNullOrEmpty(Function) ? "未关联到具体源码文件" : $"{Module}!{Function}";
                }
                return Line > 0 ? $"{SourceFile} (第 {Line} 行)" : SourceFile;
            }
            set
            {
            }
        }
    }
}
