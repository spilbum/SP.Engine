
using System;

namespace SP.Engine.Client
{
    public sealed class LatencyStats
    {
        private readonly LatencyEstimator _estimator = new LatencyEstimator();
        private readonly object _lock = new object();
        
        private uint _sentCount;
        private uint _receivedCount;
        private const uint StatsInterval = 100;

        public double LastRttMs { get; private set; }
        public double AvgRttMs { get; private set; }
        public double JitterMs { get; private set; }
        public double PacketLossRate { get; private set; }

        public void OnSent()
        {
            lock (_lock)
            {
                _sentCount++;
                if (_sentCount < StatsInterval) return;
                CalculatePeriodStats();
                ResetCounters();
            }
        }

        public void OnReceived(double rawRtt)
        {
            lock (_lock)
            {
                LastRttMs = (ushort)rawRtt;
                _receivedCount++;
                _estimator.AddSample(rawRtt);

                AvgRttMs = _estimator.SmoothedRtt;
                JitterMs = _estimator.Jitter;
            }
        }

        private void CalculatePeriodStats()
        {
            if (_sentCount == 0) return;
            var loss = (double)(_sentCount - _receivedCount) / _sentCount;
            PacketLossRate = Math.Max(0, loss);
        }

        private void ResetCounters()
        {
            _sentCount = 0;
            _receivedCount = 0;
        }
    }
}
