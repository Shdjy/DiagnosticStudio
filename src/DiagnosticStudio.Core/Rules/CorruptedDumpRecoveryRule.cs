using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 未收尾或截断损坏转储文件启发式恢复提示规则
    /// </summary>
    public sealed class CorruptedDumpRecoveryRule : IDiagnosticRule
    {
        public string RuleName => "CorruptedDumpRecoveryRule";
        public string Description => "检测转储文件是否为未完成目录回写的截断文件并标记恢复状态";

        /// <summary>
        /// 评估转储报告是否命中启发式自愈恢复
        /// </summary>
        /// <param name="dumpReport">转储报告模型</param>
        /// <returns>诊断发现集合</returns>
        public IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport)
        {
            if (dumpReport != null && dumpReport.IsRecovered)
            {
                yield return new DiagnosticFinding
                {
                    Level = DiagnosticLevel.Warning,
                    Confidence = 1.0,
                    Title = "转储文件索引未闭合（已通过启发式引擎自动修复解析）",
                    Description = "检测到转储文件的 MiniDump 流目录索引未回写（目录项全为 0）。本工具已成功通过底层特征扫描还原了崩溃异常记录、系统元数据与各线程堆栈。这通常是因为 Dump 正在落盘时进程被外部强杀、看门狗超时 Kill、写入钩子自身崩溃或断电导致未完成收尾。",
                    Recommendations = new List<string>
                    {
                        "若配置了 Full Dump (DumpType=2)，写入数百兆耗时较长，请检查外部守护进程或看门狗是否设置了过短的超时强杀限制（建议延长或临时关闭强杀），确保 WerFault.exe 完整落盘",
                        "推荐在注册表中将 LocalDumps 设置为仅针对目标程序（如 LocalDumps\\Server.exe），或切换为 MiniDump + 间接内存引用以实现百毫秒极速落盘"
                    }
                };
            }
        }
    }
}
