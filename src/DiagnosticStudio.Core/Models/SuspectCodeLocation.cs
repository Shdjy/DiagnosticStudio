using System.Collections.Generic;

namespace DiagnosticStudio.Core.Models
{
    /// <summary>
    /// 疑似代码泄漏点精准定位实体 (Suspect Code Leak Location)
    /// </summary>
    public sealed class SuspectCodeLocation
    {
        /// <summary>
        /// 责任线程 ID
        /// </summary>
        public uint ThreadId { get; set; }

        /// <summary>
        /// 线程角色/特征 (如 业务循环主线程 / 工作线程)
        /// </summary>
        public string ThreadRole { get; set; } = string.Empty;

        /// <summary>
        /// 责任模块名 (如 CppCrashTarget.exe / MyProcess.dll)
        /// </summary>
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>
        /// 责任函数名 (包含命名空间与类名，如 CppCrashTarget::SlowMemoryLeakScenario::Execute)
        /// </summary>
        public string FunctionName { get; set; } = string.Empty;

        /// <summary>
        /// 源码文件绝对路径
        /// </summary>
        public string SourceFile { get; set; } = string.Empty;

        /// <summary>
        /// 源码行号
        /// </summary>
        public uint LineNumber { get; set; }

        /// <summary>
        /// 置信度 (0.0 ~ 1.0)
        /// </summary>
        public double Confidence { get; set; } = 0.90;

        /// <summary>
        /// 诊断归因与代码行为分析详情
        /// </summary>
        public string DiagnosisDetail { get; set; } = string.Empty;

        /// <summary>
        /// 关联的重要调用栈帧
        /// </summary>
        public List<StackFrameInfo> RelatedFrames { get; set; } = new List<StackFrameInfo>();

        /// <summary>
        /// 简要显示文本：模块!函数 (源文件名:第行号行)
        /// 增加安全空 setter，支持 WPF 默认 TwoWay 绑定，防止运行时抛出只读绑定异常
        /// </summary>
        public string DisplayLocation
        {
            get
            {
                if (string.IsNullOrEmpty(FunctionName))
                {
                    return "未知责任函数";
                }
                string fileName = string.IsNullOrEmpty(SourceFile) ? "" : System.IO.Path.GetFileName(SourceFile);
                if (!string.IsNullOrEmpty(fileName) && LineNumber > 0)
                {
                    return $"{ModuleName}!{FunctionName} ({fileName}:{LineNumber})";
                }
                return $"{ModuleName}!{FunctionName}";
            }
            set
            {
            }
        }
    }
}
