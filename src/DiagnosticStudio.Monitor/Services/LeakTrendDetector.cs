using System;
using System.Collections.Generic;
using System.Linq;
using DiagnosticStudio.Core.Models;

namespace DiagnosticStudio.Monitor.Services
{
    /// <summary>
    /// 基于最小二乘线性回归与统计学的智能泄漏趋势识别器
    /// </summary>
    public sealed class LeakTrendDetector : ILeakTrendDetector
    {
        // 判定所需的最少采样点数
        private const int MinSamples = 10;

        public List<DiagnosticFinding> DetectLeakTrends(IReadOnlyList<MemoryMetrics> history, int processId)
        {
            var findings = new List<DiagnosticFinding>();
            if (history == null || history.Count < MinSamples)
            {
                return findings;
            }

            // 取最近 60 个点进行滑动窗口回归分析
            var window = history.Skip(Math.Max(0, history.Count - 60)).ToList();
            int n = window.Count;

            double[] timeSeconds = new double[n];
            DateTime t0 = window[0].Timestamp;
            for (int i = 0; i < n; i++)
            {
                timeSeconds[i] = (window[i].Timestamp - t0).TotalSeconds;
            }

            // 1. 私有提交内存 (Private Bytes) 斜率计算 (bytes/sec)
            double[] privateBytes = window.Select(w => (double)w.PrivateBytes).ToArray();
            double memorySlopePerSec = CalculateLinearSlope(timeSeconds, privateBytes);
            double memoryGrowthPerMin = memorySlopePerSec * 60; // 字节/分钟

            // 若每分钟平均增长大于 3 MB 且末值高于初值
            if (memoryGrowthPerMin > 3.0 * 1024 * 1024 && window.Last().PrivateBytes > window.First().PrivateBytes)
            {
                double mbPerMin = memoryGrowthPerMin / (1024 * 1024);
                long totalGrowth = window.Last().PrivateBytes - window.First().PrivateBytes;

                var finding = new DiagnosticFinding
                {
                    Level = DiagnosticLevel.High,
                    Title = $"🟠 运行期私有内存单调持续增长 (Memory Leak Trend: +{mbPerMin:F2} MB/min)",
                    Description = $"目标进程 PID {processId} 在近期的采样窗口内，私有提交内存 (Private Bytes) 呈现无回落的单调线性增长趋势，平均增长速率约为 +{mbPerMin:F2} MB/分钟，窗口内累计净增 {(double)totalGrowth / (1024 * 1024):F2} MB。",
                    Confidence = 0.85
                };

                finding.Evidence.Add($"监控窗口点数: {n} 点 (约 {timeSeconds.Last():F0} 秒)");
                finding.Evidence.Add($"起始私有内存: {(double)window.First().PrivateBytes / (1024 * 1024):F2} MB");
                finding.Evidence.Add($"当前私有内存: {(double)window.Last().PrivateBytes / (1024 * 1024):F2} MB");
                finding.Evidence.Add($"线性回归斜率: +{memorySlopePerSec / 1024:F2} KB/s");

                finding.Recommendations.Add("建议点击【一键抓取快照】按钮生成当前运行期 Dump，进入 Dump Analyzer 深度排查堆对象分布。");
                finding.Recommendations.Add("若为 C++ 程序，排查循环处理函数、长周期单例或全局 STL 容器（如 std::vector / std::map）是否存在只插入不释放的情况。");
                finding.Recommendations.Add("若为 C# 程序，排查静态事件监听 (Event Handler) 未取消订阅导致的跨代内存滞留。");

                findings.Add(finding);
            }

            // 2. GDI 句柄泄漏检测 (GDI Objects)
            double[] gdiObjects = window.Select(w => (double)w.GdiObjects).ToArray();
            double gdiSlope = CalculateLinearSlope(timeSeconds, gdiObjects); // 个/秒

            int currentGdi = window.Last().GdiObjects;
            if (currentGdi > 8000 || (gdiSlope > 0.5 && currentGdi > window.First().GdiObjects + 50))
            {
                var finding = new DiagnosticFinding
                {
                    Level = currentGdi > 9000 ? DiagnosticLevel.Critical : DiagnosticLevel.High,
                    Title = $"🔴 GDI 句柄持续异常增长 (GDI Object Leak: 当前 {currentGdi} / 上限 10,000)",
                    Description = $"目标进程 PID {processId} 当前 GDI 句柄数已达 {currentGdi} 个，且仍在持续上升。Windows 对单进程 GDI 对象的硬上限通常为 10,000，耗尽将导致主界面绘制失败、界面全白或崩溃闪退。",
                    Confidence = 0.95
                };

                finding.Evidence.Add($"当前 GDI 句柄: {currentGdi}");
                finding.Evidence.Add($"起始 GDI 句柄: {window.First().GdiObjects}");
                finding.Evidence.Add($"增长斜率: +{gdiSlope * 60:F1} 个/分钟");

                finding.Recommendations.Add("排查界面重绘事件 (OnPaint / PaintEventHandler / DrawItem) 中创建的 Pen / Brush / Font / Bitmap / HBITMAP 是否未调用 DeleteObject() 显式销毁。");
                finding.Recommendations.Add("C# 界面排查创建的 System.Drawing.Graphics 或 Bitmap 是否未置于 using 块内释放。");

                findings.Add(finding);
            }

            // 3. 系统句柄 (Handle Count) 增长检测
            double[] handles = window.Select(w => (double)w.HandleCount).ToArray();
            double handleSlope = CalculateLinearSlope(timeSeconds, handles);
            if (handleSlope > 2.0 && window.Last().HandleCount > window.First().HandleCount + 100)
            {
                findings.Add(new DiagnosticFinding
                {
                    Level = DiagnosticLevel.Warning,
                    Title = $"🟡 系统句柄持续泄漏预警 (Handle Leak: +{handleSlope * 60:F0} handles/min)",
                    Description = $"目标进程系统句柄数呈现持续快速上升，当前句柄数为 {window.Last().HandleCount}，净增 {window.Last().HandleCount - window.First().HandleCount} 个。可能存在未关闭的文件、事件句柄或线程未及时 Detach。",
                    Confidence = 0.80
                });
            }

            return findings;
        }

        private static double CalculateLinearSlope(double[] x, double[] y)
        {
            int n = x.Length;
            if (n < 2)
            {
                return 0.0;
            }

            double xMean = x.Average();
            double yMean = y.Average();

            double numerator = 0.0;
            double denominator = 0.0;

            for (int i = 0; i < n; i++)
            {
                double xDiff = x[i] - xMean;
                numerator += xDiff * (y[i] - yMean);
                denominator += xDiff * xDiff;
            }

            if (Math.Abs(denominator) < 1e-9)
            {
                return 0.0;
            }

            return numerator / denominator;
        }
    }
}
