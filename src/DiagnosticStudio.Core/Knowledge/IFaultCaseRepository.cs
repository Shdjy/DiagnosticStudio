using System.Collections.Generic;

namespace DiagnosticStudio.Core.Knowledge
{
    /// <summary>
    /// 故障案例知识库仓储接口
    /// </summary>
    public interface IFaultCaseRepository
    {
        void AddCase(FaultCaseItem item);
        bool RemoveCase(string caseId);
        FaultCaseItem? GetCaseById(string caseId);
        IReadOnlyList<FaultCaseItem> GetAllCases();
    }

    /// <summary>
    /// 基于内存与可选本地 JSON 文件持久化的案例知识库仓储
    /// </summary>
    public sealed class InMemoryFaultCaseRepository : IFaultCaseRepository
    {
        private readonly List<FaultCaseItem> _cases = new List<FaultCaseItem>();
        private readonly object _lock = new object();

        public InMemoryFaultCaseRepository()
        {
            // 内置工业上位机典型故障案例种子库
            SeedDefaults();
        }

        private void SeedDefaults()
        {
            AddCase(new FaultCaseItem
            {
                CaseId = "CASE-SEED-001",
                Title = "工业相机取图线程空指针解引用 (Frame Buffer nullptr)",
                ExceptionCode = 0xC0000005,
                FaultingModule = "CameraModule.dll",
                FaultingFunction = "HikCamera::RetrieveFrame",
                RootCauseCategory = "NullPointer",
                RootCauseDetail = "相机掉线或超时未取到图时，底层 SDK 返回空指针，上层未做 nullptr 判空即调用 memcpy 导致访问 0x00000000 崩溃。",
                SolutionDescription = "在取图后增加帧缓冲区有效性检验：if (pFrame == nullptr || pFrame->pData == nullptr) { return ErrorCode_FrameCorrupted; }",
                Tags = new List<string> { "相机", "空指针", "现场掉线" }
            });

            AddCase(new FaultCaseItem
            {
                CaseId = "CASE-SEED-002",
                Title = "缺陷检测算法多线程竞争死锁 (CriticalSection Lock Order Deadlock)",
                ExceptionCode = 0xC0000005,
                FaultingModule = "VisionAlgo.dll",
                FaultingFunction = "DefectDetector::ProcessMultiRois",
                RootCauseCategory = "Deadlock",
                RootCauseDetail = "检测线程 1 先锁定模型锁 m_modelLock 再锁图像缓存锁 m_bufferLock；而落盘线程 2 先锁 m_bufferLock 再获取 m_modelLock，两线程逆向加锁导致死锁卡死。",
                SolutionDescription = "重构加锁顺序，全局统一锁持有次序，或采用 std::scoped_lock(m_modelLock, m_bufferLock) 自动避免死锁。",
                Tags = new List<string> { "算法", "死锁", "多线程" }
            });
        }

        public void AddCase(FaultCaseItem item)
        {
            if (item == null)
            {
                return;
            }

            lock (_lock)
            {
                _cases.RemoveAll(c => c.CaseId == item.CaseId);
                _cases.Add(item);
            }
        }

        public bool RemoveCase(string caseId)
        {
            lock (_lock)
            {
                return _cases.RemoveAll(c => c.CaseId == caseId) > 0;
            }
        }

        public FaultCaseItem? GetCaseById(string caseId)
        {
            lock (_lock)
            {
                return _cases.Find(c => c.CaseId == caseId);
            }
        }

        public IReadOnlyList<FaultCaseItem> GetAllCases()
        {
            lock (_lock)
            {
                return _cases.ToArray();
            }
        }
    }
}
