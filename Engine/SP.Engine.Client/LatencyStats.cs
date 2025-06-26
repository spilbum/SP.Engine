using System;
using SP.Common;

namespace SP.Engine.Client
{
    public class LatencyStats
    {
        private readonly DataSampler _sampler;
        private int _pingSent;
        private int _pongReceived;

        public double MinRttMs => _sampler.Min;
        public double MaxRttMs => _sampler.Max;
        public double AvgRttMs => _sampler.Avg;
        public double JitterMs => _sampler.StdDev;
        public float PacketLossRate => _pingSent == 0 ? 0f : 1f - (float)_pongReceived / _pingSent;
        
        public LatencyStats(int windowSize = 20)
        {
            _sampler = new DataSampler(windowSize);
        }

        public void OnPingSent() => _pingSent++;
        public void OnPongReceived(double rawRtt)
        {
            _pongReceived++;
            _sampler.Add(rawRtt);
        }

        public void Clear()
        {
            _sampler.Clear();
            _pingSent = 0;
            _pongReceived = 0;
        }

        public string ToSummaryString()
            => $"Avg: {AvgRttMs:F1} Min: {MinRttMs:F1} | Max: {MaxRttMs:F1} | Jitter: {JitterMs:F1} | PackLossRate: {PacketLossRate:F1}";
    }
}
