using System;
using SP.Common;

namespace SP.Engine.Client
{
    public class TrafficInfo
    {
        public long SentBytes;
        public long ReceivedBytes;
    }
    
    public class LatencyStats
    {
        private readonly DataSampler _sampler;
        private readonly EwmaFilter _srtt;
        private int _sentCount;
        private int _receivedCount;

        public double LastRttMs { get; private set; }
        public double SmoothedRttMs => _srtt.Value;
        public double MinRttMs => _sampler.Min;
        public double MaxRttMs => _sampler.Max;
        public double AvgRttMs => _sampler.Avg;
        public double JitterMs => _sampler.StdDev;
        public float PacketLossRate => _sentCount == 0 ? 0f : 1f - (float)_receivedCount / _sentCount;
        
        public LatencyStats(int windowSize = 20)
        {
            _sampler = new DataSampler(windowSize);
            _srtt = new EwmaFilter(0.125);
        }

        public void OnSent() => _sentCount++;
        public void OnReceived(double rawRtt)
        {
            LastRttMs = rawRtt;
            _receivedCount++;
            _sampler.Add(rawRtt);
            _srtt.Update(rawRtt);
        }

        public void Clear()
        {
            _sampler.Clear();
            _srtt.Reset();
            _sentCount = 0;
            _receivedCount = 0;
            LastRttMs = 0;
        }
   }
}
