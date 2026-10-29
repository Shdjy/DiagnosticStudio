using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace DiagnosticStudio.Core.Industrial
{
    /// <summary>
    /// 高性能工业事件环形缓冲区记录器实现
    /// 支持容量硬限制、旧数据平滑轮转覆盖与零内存泄漏
    /// </summary>
    public sealed class IndustrialTimelineRecorder : IIndustrialTimelineRecorder
    {
        private readonly IndustrialTimelineEvent[] _buffer;
        private readonly int _capacity;
        private readonly object _lock = new object();

        private int _head = 0;
        private int _count = 0;
        private long _sequenceCounter = 0;

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _count;
                }
            }
        }

        public int Capacity => _capacity;

        public IndustrialTimelineRecorder(int capacity = 50000)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "容量必须大于 0");
            }

            _capacity = capacity;
            _buffer = new IndustrialTimelineEvent[capacity];
        }

        public void RecordEvent(IndustrialTimelineEvent ev)
        {
            if (ev == null)
            {
                return;
            }

            lock (_lock)
            {
                ev.SequenceNumber = Interlocked.Increment(ref _sequenceCounter);
                if (ev.MonotonicTimestampNs == 0)
                {
                    ev.MonotonicTimestampNs = Stopwatch.GetTimestamp();
                }

                _buffer[_head] = ev;
                _head = (_head + 1) % _capacity;
                if (_count < _capacity)
                {
                    _count++;
                }
            }
        }

        public void Record(
            string moduleName,
            IndustrialEventType eventType,
            long frameId = 0,
            string taskId = "",
            double durationMs = 0,
            int statusCode = 0,
            string? errorMessage = null)
        {
            var ev = new IndustrialTimelineEvent
            {
                ModuleName = moduleName ?? string.Empty,
                EventType = eventType,
                FrameId = frameId,
                TaskId = taskId ?? string.Empty,
                DurationMs = durationMs,
                StatusCode = statusCode,
                ErrorMessage = errorMessage,
                ProcessId = Environment.ProcessId,
                ThreadId = Environment.CurrentManagedThreadId,
                WallClockTime = DateTime.UtcNow,
                MonotonicTimestampNs = Stopwatch.GetTimestamp()
            };

            RecordEvent(ev);
        }

        public IReadOnlyList<IndustrialTimelineEvent> GetAllEvents()
        {
            lock (_lock)
            {
                var list = new List<IndustrialTimelineEvent>(_count);
                if (_count == 0)
                {
                    return list;
                }

                int start = (_count < _capacity) ? 0 : _head;
                for (int i = 0; i < _count; i++)
                {
                    int idx = (start + i) % _capacity;
                    if (_buffer[idx] != null)
                    {
                        list.Add(_buffer[idx]);
                    }
                }

                return list;
            }
        }

        public IReadOnlyList<IndustrialTimelineEvent> GetRecentEvents(TimeSpan window)
        {
            var all = GetAllEvents();
            if (all.Count == 0)
            {
                return all;
            }

            DateTime threshold = DateTime.UtcNow.Subtract(window);
            var results = new List<IndustrialTimelineEvent>();

            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (all[i].WallClockTime >= threshold)
                {
                    results.Add(all[i]);
                }
                else
                {
                    break;
                }
            }

            results.Reverse();
            return results;
        }

        public void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _head = 0;
                _count = 0;
            }
        }
    }
}
