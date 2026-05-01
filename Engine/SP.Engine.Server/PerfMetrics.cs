using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using SP.Core;

namespace SP.Engine.Server
{
    public class PerfMetrics
    {
        public DateTime TimestampUtc { get; set; }
        public double CpuUsagePercent { get; set; }
        public long WorkingSetBytes { get; set; }
        public long ManagedHeapBytes { get; set; }

        public int[] GcCounts { get; set; } = new int[3]; // 누적 카운팅
        public int[] GcDelta { get; set; } = new int[3]; // 이전 샘플링 이후 발생 횟수
        public int ThreadCount { get; set; }
        
        public int SessionCount { get; set; }
        public long ActiveBufferCount { get; set; }

        public double AvgFiberDwellMs { get; set; }
        public double AvgFiberExecMs { get; set; }
        public int PendingJobTotal { get; set; }

        public override string ToString()
        {
            var t = TimestampUtc.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            return $"[{t}] CPU:{CpuUsagePercent:F1}% " +
                   $"WS:{FormatBytes(WorkingSetBytes)} " +
                   $"Heap:{FormatBytes(ManagedHeapBytes)} " +
                   $"GC_Delta(0/1/2):{GcDelta[0]}/{GcDelta[1]}/{GcDelta[2]} " +
                   $"Threads:{ThreadCount} " +
                   $"Sessions:{SessionCount} | Buffers:{ActiveBufferCount} " +
                   $"Fiber: [AvgDwell:{AvgFiberDwellMs:F2}ms | AvgExec:{AvgFiberExecMs:F2}ms | PendingJobs:{PendingJobTotal}]";
        }

        private static string FormatBytes(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            var i = 0;
            while (v >= 1024 && i < u.Length - 1)
            {
                v /= 1024;
                i++;
            }

            return v.ToString("N2", CultureInfo.InvariantCulture) + u[i];
        }
    }

    public class PerfMetricsSampler : IDisposable
    {
        private readonly object _lock = new object();
        private readonly Process _proc = Process.GetCurrentProcess();
        private readonly int _processorCount = Environment.ProcessorCount;
        private bool _initialized;
        private DateTime _prevTimestampUtc;
        private TimeSpan _prevTotalCpu;
        private readonly int[] _prevGcCounts = new int[3];

        public void Dispose() => _proc?.Dispose();

        public void Initialize()
        {
            lock (_lock)
            {
                _proc.Refresh();
                _prevTotalCpu = _proc.TotalProcessorTime;
                _prevTimestampUtc = DateTime.UtcNow;

                for (var i = 0; i < 3; i++)
                    _prevGcCounts[i] = GC.CollectionCount(i);
                
                _initialized = true;
            }
        }

        public PerfMetrics Sample(int sessionCount, PeerManager manager)
        {
            lock (_lock)
            {
                if (!_initialized) Initialize();
                
                var now = DateTime.UtcNow;
                _proc.Refresh();

                var totalCpu = _proc.TotalProcessorTime;
                var deltaCpu = totalCpu - _prevTotalCpu;
                var deltaWall = now - _prevTimestampUtc;
                var cpuPercent = 0.0;
                
                if (_initialized && deltaWall.TotalMilliseconds > 10)
                {
                    // 프로세스가 소비한 CPU 시간 / (경과 실제 시간 * 코어 수)
                    cpuPercent = deltaCpu.TotalMilliseconds / (deltaWall.TotalMilliseconds * _processorCount) * 100.0;
                    cpuPercent = Math.Clamp(cpuPercent, 0, 100);
                }

                var currentGcCounts = new int[3];
                var deltaGc = new int[3];
                for (var i = 0; i < 3; i++)
                {
                    currentGcCounts[i] = GC.CollectionCount(i);
                    deltaGc[i] = currentGcCounts[i] - _prevGcCounts[i];
                    _prevGcCounts[i] = currentGcCounts[i];
                }

                var activeBuf = BufferMetrics.GetRentCount();

                var fiberMetrics = manager.GetGlobalFiberMetrics();
                
                var metrics = new PerfMetrics
                {
                    TimestampUtc = now,
                    CpuUsagePercent = cpuPercent,
                    WorkingSetBytes = _proc.WorkingSet64,
                    ManagedHeapBytes = GC.GetTotalMemory(false),
                    GcCounts = currentGcCounts,
                    GcDelta = deltaGc,
                    ThreadCount = _proc.Threads.Count,
                    SessionCount = sessionCount,
                    ActiveBufferCount = activeBuf,
                    AvgFiberDwellMs = fiberMetrics.avgDwell,
                    AvgFiberExecMs = fiberMetrics.avgExec,
                    PendingJobTotal = fiberMetrics.pendingTotal
                };

                _prevTotalCpu = totalCpu;
                _prevTimestampUtc = now;
                return metrics;
            }
        }
    }

    public class PerfMonitor : IDisposable
    {
        private readonly PerfMetricsSampler _sampler = new PerfMetricsSampler();
        private volatile PerfMetrics _last;
        private int _sampling;

        public PerfMonitor()
        {
            _sampler.Initialize();
        }

        public void Dispose()
        {
            _sampler.Dispose();
        }

        public event Action<PerfMetrics> OnSampled;

        public PerfMetrics GetLast()
        {
            return _last;
        }

        public bool TryGetLast(out PerfMetrics metrics)
        {
            metrics = _last;
            return metrics != null;
        }

        public void Tick(int sessionCount, PeerManager manager)
        {
            if (Interlocked.Exchange(ref _sampling, 1) == 1)
                return;

            try
            {
                var m = _sampler.Sample(sessionCount, manager);
                _last = m;
                var handler = OnSampled;
                handler?.Invoke(_last);
            }
            catch
            {
                // ignored
            }
            finally
            {
                Interlocked.Exchange(ref _sampling, 0);
            }
        }
    }
}
