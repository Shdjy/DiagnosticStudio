using System.Collections.Generic;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Rules
{
    /// <summary>
    /// 未收尾或截断损坏转储文件启发式恢复提示规则
    /// 明确标识 Dump 处于启发式自愈恢复状态，提示推导边界与局限性
    /// </summary>
    public sealed class CorruptedDumpRecoveryRule : IDiagnosticRule
    {
        public string RuleName => "CorruptedDumpRecoveryRule";

        public string RuleId => "RULE-DUMP-REC-001";

        public string Version => "2.0.0";

        public string Description => "检测转储文件是否为未完成目录回写的截断文件，标记恢复状态与数据可靠性边界。";

        /// <summary>
        /// 评估转储报告是否命中启发式自愈恢复
        /// </summary>
        /// <param name="dumpReport">转储报告模型</param>
        /// <returns>诊断发现集合</returns>
        public IEnumerable<DiagnosticFinding> Evaluate(DumpReportModel dumpReport)
        {
            if (dumpReport == null)
            {
                yield break;
            }

            if (dumpReport.IsRecovered || dumpReport.Integrity.Status == DumpIntegrityStatus.HeuristicallyRecovered)
            {
                dumpReport.Integrity.Status = DumpIntegrityStatus.HeuristicallyRecovered;
                dumpReport.Integrity.RecoveryNotes.Add("MiniDump 流目录全 0，已通过物理内存与上下文签名扫描进行启发式恢复。");

                var finding = new DiagnosticFinding
                {
                    RuleId = RuleId,
                    RuleVersion = Version,
                    Level = DiagnosticLevel.Warning,
                    Confidence = 0.95,
                    Title = "⚠️ 转储文件索引未闭合（已通过启发式引擎容错自愈）",
                    Description = "检测到转储文件的 MiniDump 流目录索引未回写（目录项全为 0）。工具已成功通过底层特征扫描还原了崩溃异常记录、系统元数据与各线程堆栈。\n" +
                                  "【可信度边界提示】由于依赖物理特征重建，部分非连续内存页或深层栈帧可能存在误匹配风险，分析结论建议结合源码与日志交叉验证。"
                };

                finding.AddStructuredEvidence(
                    EvidenceCategory.ConfirmedFact,
                    "MiniDump 文件头部目录偏移指向全 0 区域，符合落盘中途强杀特征",
                    1.0);

                finding.AddStructuredEvidence(
                    EvidenceCategory.Hypothesis,
                    "推测原因：Dump 正在落盘时进程被外部看门狗/守护进程强行 Kill，或断电/磁盘满导致未正常收尾",
                    0.85);

                finding.AddStructuredEvidence(
                    EvidenceCategory.VerificationStep,
                    "检查看门狗超时时间配置，确保有充足时间（如 > 10秒）供 WerFault 写入 Dump",
                    1.0);

                finding.Recommendations.Add("若配置了 Full Dump (DumpType=2)，写入数百兆耗时较长，请检查外部守护进程或看门狗是否设置了过短的超时强杀限制（建议延长或临时关闭强杀），确保 WerFault.exe 完整落盘。");
                finding.Recommendations.Add("推荐在注册表中将 LocalDumps 设置为仅针对目标程序（如 LocalDumps\\Server.exe），或切换为 MiniDump + 间接内存引用以实现百毫秒极速落盘。");

                yield return finding;
            }
        }
    }
}
