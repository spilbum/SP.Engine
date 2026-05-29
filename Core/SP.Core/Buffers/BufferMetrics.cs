using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SP.Core.Buffers
{
    public static class BufferMetrics
    {
        [ThreadStatic] private static PaddedCounter _localCounter;

        private static readonly ConcurrentBag<PaddedCounter> _counters = new ConcurrentBag<PaddedCounter>();

        private class PaddedCounter
        {
            public long RentCount;
            public long ReturnCount;
        }

        private static PaddedCounter GetLocalCounter()
        {
            if (_localCounter != null) return _localCounter;
            _localCounter = new PaddedCounter();
            _counters.Add(_localCounter);
            return _localCounter;
        }

        public static void OnRent() => GetLocalCounter().RentCount++;
        public static void OnReturn() => GetLocalCounter().ReturnCount++;

        public static (long TotalRent, long TotalReturn, long ActiveRent) Snapshot()
        {
            long totalRent = 0, totalReturn = 0;

            foreach (var counter in _counters)
            {
                totalRent += Volatile.Read(ref counter.RentCount);
                totalReturn += Volatile.Read(ref counter.ReturnCount);
            }
            
            var active = totalRent - totalReturn;
            return (totalRent, totalReturn, active < 0 ? 0 : active);
        }
    }
}
