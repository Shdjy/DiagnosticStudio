using System;
using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Core.Industrial
{
    /// <summary>
    /// 工业时序跨来源关联分析引擎实现
    /// </summary>
    public sealed class TimelineCorrelator : ITimelineCorrelator
    {
        public IReadOnlyList<FrameLifecycleTrace> TraceFrames(IReadOnlyList<IndustrialTimelineEvent> events)
        {
            var traces = new List<FrameLifecycleTrace>();
            if (events == null || events.Count == 0)
            {
                return traces;
            }

            // 过滤并按 FrameId 分组 (排除 FrameId <= 0 的全局系统事件)
            var groups = events.Where(e => e.FrameId > 0)
                               .GroupBy(e => e.FrameId)
                               .OrderBy(g => g.Key);

            foreach (var g in groups)
            {
                var sorted = g.OrderBy(e => e.MonotonicTimestampNs).ToList();
                var first = sorted[0];
                var last = sorted[^1];

                double totalDuration = sorted.Sum(e => e.DurationMs);
                if (totalDuration <= 0 && sorted.Count > 1)
                {
                    totalDuration = (last.WallClockTime - first.WallClockTime).TotalMilliseconds;
                }

                traces.Add(new FrameLifecycleTrace
                {
                    FrameId = g.Key,
                    TaskId = sorted.FirstOrDefault(e => !string.IsNullOrEmpty(e.TaskId))?.TaskId ?? string.Empty,
                    StartTime = first.WallClockTime,
                    TotalPipelineDurationMs = totalDuration,
                    Events = sorted
                });
            }

            return traces;
        }

        public List<DiagnosticFinding> CorrelateAndDiagnose(
            IReadOnlyList<IndustrialTimelineEvent> events,
            DateTime? crashTime = null,
            double maxExpectedAlgoDurationMs = 100.0)
        {
            var findings = new List<DiagnosticFinding>();
            if (events == null || events.Count == 0)
            {
                return findings;
            }

            // 1. 统计算法超时事件 (Algorithm Overtime)
            var overtimeAlgos = events.Where(e =>
                (e.EventType == IndustrialEventType.AlgoCompleted || e.EventType == IndustrialEventType.AlgoStart) &&
                e.DurationMs > maxExpectedAlgoDurationMs).ToList();

            if (overtimeAlgos.Count > 0)
            {
                var worst = overtimeAlgos.OrderByDescending(e => e.DurationMs).First();
                var finding = new DiagnosticFinding
                {
                    RuleId = "IND-ALGO-TIMEOUT-001",
                    Level = DiagnosticLevel.High,
                    Title = $"⏱️ 工业算法推理耗时突增超时 (最长耗时: {worst.DurationMs:F1}ms / 阈值: {maxExpectedAlgoDurationMs:F0}ms)",
                    Description = $"在业务事件流中检测到 {overtimeAlgos.Count} 处算法耗时超出节拍上限 ({maxExpectedAlgoDurationMs:F0}ms)。\n" +
                                  $"首要异常帧 FrameId: {worst.FrameId} (模块: {worst.ModuleName})，耗时高达 {worst.DurationMs:F1}ms。\n" +
                                  "算法超时会引发图像接收队列积压、丢帧或 PLC 剔废信号错位错过气缸物理击打窗口。",
                    Confidence = 0.90
                };

                finding.AddStructuredEvidence(
                    EvidenceCategory.ConfirmedFact,
                    $"超时总频次: {overtimeAlgos.Count} 次，峰值耗时帧: Frame {worst.FrameId} ({worst.DurationMs:F1}ms)",
                    1.0);

                finding.AddStructuredEvidence(
                    EvidenceCategory.SupportingEvidence,
                    $"责任模块: {worst.ModuleName}，发生时间: {worst.WallClockTime:HH:mm:ss.fff}",
                    0.9);

                finding.AddStructuredEvidence(
                    EvidenceCategory.VerificationStep,
                    "检查该异常帧所对应的缺陷图像尺寸或目标数量是否异常，排查算法内部大图深拷贝或模型推理显存颠簸",
                    1.0);

                finding.Recommendations.Add("针对复杂工件大图启用 ROI 感兴趣区域裁剪，避免对整幅大视场全分辨率图像执行高耗时遍历。");
                finding.Recommendations.Add("对算法推理设置硬超时控制，超时时执行降级保底策略，防止工作线程被死锁挂起。");

                findings.Add(finding);
            }

            // 2. 统计 PLC 通信异常与心跳超时
            var plcErrors = events.Where(e =>
                e.EventType == IndustrialEventType.PlcHeartbeatTimeout ||
                (e.ModuleName.Contains("plc", StringComparison.OrdinalIgnoreCase) && e.StatusCode != 0)).ToList();

            if (plcErrors.Count > 0)
            {
                var firstErr = plcErrors[0];
                var finding = new DiagnosticFinding
                {
                    RuleId = "IND-PLC-COMM-001",
                    Level = DiagnosticLevel.Critical,
                    Title = $"⚡ PLC 工业通信中断或状态码异常 (累计异常: {plcErrors.Count} 起)",
                    Description = $"工业通信链路中检测到 PLC 心跳握手失败或通信指令错误响应。\n" +
                                  $"首发错误时间: {firstErr.WallClockTime:HH:mm:ss.fff}，模块: {firstErr.ModuleName}，错误信息: {firstErr.ErrorMessage ?? "未知网络通信故障"} (Code: {firstErr.StatusCode})。\n" +
                                  "PLC 通信阻塞将导致上位机工件同步错位、传送带停机或引发上位机网络同步死锁。",
                    Confidence = 0.95
                };

                finding.AddStructuredEvidence(
                    EvidenceCategory.ConfirmedFact,
                    $"PLC 通信异常首发于: {firstErr.WallClockTime:HH:mm:ss.fff}，状态码: {firstErr.StatusCode}",
                    1.0);

                finding.AddStructuredEvidence(
                    EvidenceCategory.VerificationStep,
                    "检查现场工业以太网网线、交换机端口状态与 PLC IP 地址冲突，复核上位机重试连接机制",
                    1.0);

                findings.Add(finding);
            }

            // 3. 关联崩溃发生时刻前夕的事件异动
            if (crashTime.HasValue)
            {
                DateTime windowStart = crashTime.Value.AddSeconds(-5);
                var preCrashEvents = events.Where(e => e.WallClockTime >= windowStart && e.WallClockTime <= crashTime.Value).ToList();
                var preCrashErrors = preCrashEvents.Where(e => e.StatusCode != 0).ToList();

                if (preCrashErrors.Count > 0)
                {
                    var lastErr = preCrashErrors[^1];
                    var finding = new DiagnosticFinding
                    {
                        RuleId = "IND-PRE-CRASH-CORR-001",
                        Level = DiagnosticLevel.High,
                        Title = $"🔍 崩溃发生前夕关键业务异常链 (Pre-Crash Activity Correlation)",
                        Description = $"在进程崩溃发生前 5 秒内，业务事件时序轴记录到 {preCrashErrors.Count} 处硬件/算法异常。\n" +
                                      $"崩溃前最后一次故障事件发生在崩溃前 {(crashTime.Value - lastErr.WallClockTime).TotalMilliseconds:F0}ms，" +
                                      $"模块: [{lastErr.ModuleName}]，事件: {lastErr.EventType}，描述: {lastErr.ErrorMessage}。\n" +
                                      "该事件具有高度因果嫌疑，极大可能为诱发后续崩溃的诱因。",
                        Confidence = 0.88
                    };

                    finding.AddStructuredEvidence(
                        EvidenceCategory.SupportingEvidence,
                        $"崩溃时刻: {crashTime.Value:HH:mm:ss.fff}，前置异动时刻: {lastErr.WallClockTime:HH:mm:ss.fff}",
                        0.9);

                    finding.AddStructuredEvidence(
                        EvidenceCategory.Hypothesis,
                        "假设：业务链路在前序环节返回了无效数据指针、通信超时异常未被安全捕获或内存被破坏，随后在后续主线程中引发崩溃",
                        0.85);

                    findings.Add(finding);
                }
            }

            return findings;
        }
    }
}
