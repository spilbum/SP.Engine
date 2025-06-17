using System;
using System.Collections.Generic;
using System.Linq;
using SP.Common;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Client
{
    public enum ENetworkQuality
    {
        None = 0,
        Excellent,
        Good,
        Moderate,
        Poor,
        Unstable,
    }
    
    public class LatencyStats
    {
        private readonly EwmaTracker _ewmaTracker;
        private readonly DataSampler _dataSampler;

        public double Estimated => _ewmaTracker.Estimated;
        public double StdDev => _dataSampler.StdDev;
        public double Min => _dataSampler.Min;
        public double Max => _dataSampler.Max;
        public double Avg => _dataSampler.Avg;
        public double Jitter => _dataSampler.Jitter;
        
        public LatencyStats(int windowSize = 20)
        {
            if (windowSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(windowSize));
            _dataSampler = new DataSampler(windowSize);
            _ewmaTracker = new EwmaTracker(0.125);
        }
        
        public void Update(double rawRtt)
        {
            if (_ewmaTracker.IsInitialized)
            {
                var estimated = _ewmaTracker.Estimated;
                var clamped = Math.Clamp(rawRtt, estimated / 2.0, estimated * 2.0);
                _ewmaTracker.Update(clamped);
            }
            else
            {
                _ewmaTracker.Initialize(rawRtt);
            }
            
            _dataSampler.Add(rawRtt);
        }

        public ENetworkQuality GetQuality()
        {
            if (Estimated < 100 && Jitter < 10) return ENetworkQuality.Excellent;
            if (Estimated < 150 && Jitter < 20) return ENetworkQuality.Good;
            if (Estimated < 250) return ENetworkQuality.Moderate;
            return Estimated < 400 ? ENetworkQuality.Poor : ENetworkQuality.Unstable;
        }

        public void Clear()
        {
            _dataSampler.Clear();
            _ewmaTracker.Clear();
        }

        public string ToSummaryString()
            => $"RTT: {Estimated:F1}ms | Avg: {Avg:F1} | StdDev: {StdDev:F1} | Min: {Min:F1} | Max: {Max:F1} | Jitter: {Jitter:F1} | Quality: {GetQuality()}";
    }
}
