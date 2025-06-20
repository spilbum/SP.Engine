using System;
using SP.Common;

namespace SP.Engine.Client
{
    public class LatencyStats
    {
        private readonly EwmaTracker _ewma;
        private readonly DataSampler _sampler;
        private int _pingSent;
        private int _pongReceived;

        public double SmoothedRttMs => _ewma.Estimated;
        public double MinRttMs => _sampler.Min;
        public double MaxRttMs => _sampler.Max;
        public double AvgRttMs => _sampler.Avg;
        public double JitterMs => _sampler.StdDev;
        public float PacketLossRate => _pingSent == 0 ? 0f : 1f - (float)_pongReceived / _pingSent;
        
        public LatencyStats(int windowSize = 20)
        {
            if (windowSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(windowSize));
            _sampler = new DataSampler(windowSize);
            _ewma = new EwmaTracker(0.125);
        }

        public void OnPingSent()
        {
            _pingSent++;
        }
        
        public void OnPongReceived(double rawRtt)
        {
            _pongReceived++;
            
            if (_ewma.IsInitialized)
            {
                var est = _ewma.Estimated;
                var clamped = Math.Clamp(rawRtt, est / 2.0, est * 2.0);
                _ewma.Update(clamped);
            }
            else
            {
                _ewma.Initialize(rawRtt);
            }
            
            _sampler.Add(rawRtt);
        }

        public void Clear()
        {
            _sampler.Clear();
            _ewma.Clear();
        }

        public string ToSummaryString()
            => $"RTT: {SmoothedRttMs:F1}ms | Avg: {AvgRttMs:F1} Min: {MinRttMs:F1} | Max: {MaxRttMs:F1} | Jitter: {JitterMs:F1} | PackLossRate: {PacketLossRate:F1}";
    }
}
