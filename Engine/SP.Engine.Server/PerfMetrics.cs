using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using SP.Core.Buffers;

namespace SP.Engine.Server
{
    public struct PerfMetrics
    {
        public DateTime TimestampUtc;
        public double CpuUsagePercent;
        public long WorkingSetBytes;
        public long ManagedHeapBytes;
        
        public int GcDelta0;
        public int GcDelta1;
        public int GcDelta2;
        
        public int ThreadCount;
        public int SessionCount;
        public long ActiveBufferCount;

        public double AvgQueueLength;
        public int MaxQueueLength;
        public double AvgExecutionTimeMs;
        public double CurrentPps;
        
        public override string ToString()
        {
            return $"[ENGINE KPI] PPS: {CurrentPps:F0} | " +
                   $"AvgQueue: {AvgQueueLength:F1} | MaxQueue: {MaxQueueLength} | " +
                   $"AvgExec: {AvgExecutionTimeMs:F3}ms | ActiveBuf: {ActiveBufferCount} | " +
                   $"CPU:{CpuUsagePercent:F1}% | WS:{FormatBytes(WorkingSetBytes)} | " +
                   $"Heap:{FormatBytes(ManagedHeapBytes)} | " +
                   $"GC(0/1/2):{GcDelta0}/{GcDelta1}/{GcDelta2} | " +
                   $"Threads:{ThreadCount} | Sessions:{SessionCount}";
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

    public sealed class PerfMetricsSampler : IDisposable
    {
        private readonly object _lock = new();
        private readonly Process _proc = Process.GetCurrentProcess();
        private readonly int _processorCount = Environment.ProcessorCount;
        private bool _initialized;
        
        private DateTime _prevTimestampUtc;
        private TimeSpan _prevTotalCpu;

        private int _prevGcCount0;
        private int _prevGcCount1;
        private int _prevGcCount2;

        private long _lastProcessedCount;

        public void Dispose() => _proc?.Dispose();

        public void Initialize()
        {
            lock (_lock)
            {
                _proc.Refresh();
                _prevTotalCpu = _proc.TotalProcessorTime;
                _prevTimestampUtc = DateTime.UtcNow;

                _prevGcCount0 = GC.CollectionCount(0);
                _prevGcCount1 = GC.CollectionCount(1);
                _prevGcCount2 = GC.CollectionCount(2);
                
                _initialized = true;
            }
        }

        public PerfMetrics Sample(EngineBase engine, int sessionCount, long totalProcessed, double totalTimeMs)
        {
            lock (_lock)
            {
                if (!_initialized) Initialize();
                
                var nowUtc = DateTime.UtcNow;
                _proc.Refresh();

                // CPU 사용량 계산
                var totalCpu = _proc.TotalProcessorTime;
                var deltaCpu = totalCpu - _prevTotalCpu;
                var deltaWall = nowUtc - _prevTimestampUtc;
                var cpuPercent = 0.0;
                
                if (_initialized && deltaWall.TotalMilliseconds > 10)
                {
                    // 프로세스가 소비한 CPU 시간 / (경과 실제 시간 * 코어 수)
                    cpuPercent = deltaCpu.TotalMilliseconds / (deltaWall.TotalMilliseconds * _processorCount) * 100.0;
                    cpuPercent = Math.Clamp(cpuPercent, 0, 100);
                }
                
                var currentGc0 = GC.CollectionCount(0);
                var currentGc1 = GC.CollectionCount(1);
                var currentGc2 = GC.CollectionCount(2);

                var deltaGc0 = currentGc0 - _prevGcCount0;
                var deltaGc1 = currentGc1 - _prevGcCount1;
                var deltaGc2 = currentGc2 - _prevGcCount2;

                _prevGcCount0 = currentGc0;
                _prevGcCount1 = currentGc1;
                _prevGcCount2 = currentGc2;

                var (_, _, activeBuffers) = BufferMetrics.Snapshot();

                var deltaProcessed = totalProcessed - _lastProcessedCount;
                _lastProcessedCount = totalProcessed;

                // 수집 주기 계산
                var periodSec = deltaWall.TotalSeconds;
                var pps = periodSec > 0 ? deltaProcessed / periodSec : 0;
                
                long totalQueue = 0;
                var maxQueue = 0;
                var fiberCount = engine.LogicFiberCount;
                
                for (var i = 0; i < fiberCount; i++)
                {
                    var pending = engine.GetLogicFiberPendingCount(i);
                    totalQueue += pending;
                    if (pending > maxQueue) maxQueue = pending;
                }
                
                var avgQueue = fiberCount > 0 ? (double)totalQueue / fiberCount : 0;
                var avgExecTimeMs = totalProcessed > 0 ? totalTimeMs / totalProcessed : 0;

                var threadCount = _proc.Threads.Count;
                
                var metrics = new PerfMetrics
                {
                    TimestampUtc = nowUtc,
                    CpuUsagePercent = cpuPercent,
                    WorkingSetBytes = _proc.WorkingSet64,
                    ManagedHeapBytes = GC.GetTotalMemory(false),
                    GcDelta0 = deltaGc0,
                    GcDelta1 = deltaGc1,
                    GcDelta2 = deltaGc2,
                    ThreadCount = threadCount,
                    SessionCount = sessionCount,
                    ActiveBufferCount = activeBuffers,
                    AvgQueueLength = avgQueue,
                    MaxQueueLength = maxQueue,
                    AvgExecutionTimeMs = avgExecTimeMs,
                    CurrentPps = pps
                };

                _prevTotalCpu = totalCpu;
                _prevTimestampUtc = nowUtc;
                return metrics;
            }
        }
    }

    public class PerfMonitor : IDisposable
    {
        private readonly PerfMetricsSampler _sampler = new();
        private PerfMetrics _last;
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

        public bool TryGetLast(out PerfMetrics metrics)
        {
            metrics = _last;
            return metrics.CpuUsagePercent != 0;
        }

        public void Tick(EngineBase engine, int sessionCount, long totalProcessed, double totalTimeMs)
        {
            if (Interlocked.Exchange(ref _sampling, 1) == 1)
                return;

            try
            {
                var m = _sampler.Sample(engine, sessionCount, totalProcessed, totalTimeMs);
                _last = m;
                OnSampled?.Invoke(_last);
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
