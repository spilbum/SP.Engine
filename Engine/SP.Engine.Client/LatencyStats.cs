using System;
using SP.Core;

namespace SP.Engine.Client
{
    public class TrafficInfo
    {
        public long ReceivedBytes;
        public long SentBytes;
    }

    public sealed class LatencyStats
    {
        private LatencyEstimator _estimator = new LatencyEstimator(0.125, 0.125);
        private uint _sentCount;
        private uint _receivedCount;
        private const uint StatsInterval = 100;

        public ushort LastRttMs { get; private set; }
        public ushort AvgRttMs => (ushort)_estimator.SmoothedRtt;
        public ushort JitterMs => (ushort)_estimator.Jitter;
        public byte PacketLossRate => CalculateLossRate();

        public void OnSent()
        {
            _sentCount++;
            if (_sentCount > StatsInterval) Reset();
        }

        public void OnReceived(double rawRtt)
        {
            LastRttMs = (ushort)rawRtt;
            _receivedCount++;
            _estimator.AddSample(rawRtt);
        }

        private byte CalculateLossRate()
        {
            if (_sentCount == 0) return 0;
            var loss = (1f - (float)_receivedCount / _sentCount) * 100f;
            return (byte)Math.Clamp(loss, 0, 100);
        }
        
        private void Reset()
        {
            _sentCount = 0;
            _receivedCount = 0;
        }
    }
}
