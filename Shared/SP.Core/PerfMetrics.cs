using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace SP.Core
{
    public class PerfMetrics
    {
        public DateTime TimestampUtc { get; set; }
        public double CpuUsagePercent { get; set; }
        public long WorkingSetBytes { get; set; }
        public long ManagedHeapBytes { get; set; }
        public int ThreadCount { get; set; }

        public override string ToString()
        {
            var t = TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            return $"Time:{t} " +
                   $"CPU={CpuUsagePercent:F1}% " +
                   $"WS={FormatBytes(WorkingSetBytes)} " +
                   $"Managed={FormatBytes(ManagedHeapBytes)} " +
                   $"Threads={ThreadCount}";
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

        public void Dispose()
        {
            _proc?.Dispose();
        }

        public void Initialize()
        {
            lock (_lock)
            {
                _proc.Refresh();
                _prevTotalCpu = _proc.TotalProcessorTime;
                _prevTimestampUtc = DateTime.UtcNow;
                _initialized = true;
            }
        }

        public async Task<PerfMetrics> SampleAsync(int delayMs = 500, CancellationToken ct = default)
        {
            if (!_initialized) Initialize();
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
            return Sample();
        }

        public PerfMetrics Sample()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                _proc.Refresh();

                var totalCpu = _proc.TotalProcessorTime;
                var deltaCpu = totalCpu - _prevTotalCpu;
                var deltaWall = now - _prevTimestampUtc;

                var cpuPercent = 0.0;
                if (_initialized && deltaWall.TotalMilliseconds > 1 && _processorCount > 0)
                {
                    // 프로세스가 소비한 CPU 시간 / (경과 실제 시간 * 코어 수)
                    cpuPercent = deltaCpu.TotalMilliseconds / (deltaWall.TotalMilliseconds * _processorCount) * 100.0;
                    if (cpuPercent < 0) cpuPercent = 0;
                }

                var metrics = new PerfMetrics
                {
                    TimestampUtc = now,
                    CpuUsagePercent = cpuPercent,
                    WorkingSetBytes = _proc.WorkingSet64,
                    ManagedHeapBytes = GC.GetTotalMemory(false),
                    ThreadCount = _proc.Threads.Count
                };

                _prevTotalCpu = totalCpu;
                _prevTimestampUtc = now;
                _initialized = true;
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

        public void Tick()
        {
            if (Interlocked.Exchange(ref _sampling, 1) == 1)
                return;

            try
            {
                var m = _sampler.Sample();
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
