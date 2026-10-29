using System.Collections.Generic;

namespace DiagnosticStudio.Core.Concurrency
{
    /// <summary>
    /// 检测到的死锁环路 (Deadlock Cycle) 实体
    /// </summary>
    public sealed class DeadlockCycle
    {
        /// <summary>
        /// 构成死锁闭环的线程 ID 列表 (按等待顺序排列，如 T1 -> T2 -> T1)
        /// </summary>
        public List<uint> CycleThreadIds { get; set; } = new List<uint>();

        /// <summary>
        /// 环路中每一步的资源竞争详情说明
        /// </summary>
        public List<string> StepDescriptions { get; set; } = new List<string>();

        /// <summary>
        /// 是否已通过强特征严格证实
        /// </summary>
        public bool IsConfirmed { get; set; }

        /// <summary>
        /// 死锁简要摘要
        /// </summary>
        public string Summary => $"线程死锁闭环: {string.Join(" ➔ ", CycleThreadIds)} (共 {CycleThreadIds.Count} 条线程互锁)";
    }
}
