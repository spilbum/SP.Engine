using System;
using SP.Core;

namespace SP.Engine.Client
{
    public class LatencyEstimator
    {
        private readonly EwmaFilter _rttFilter;
        private readonly EwmaFilter _jitterFilter;
        private double _lastRttMs;

        public LatencyEstimator(double rttAlpha = 0.125, double jitterAlpha = 0.125)
        {
            _rttFilter = new EwmaFilter(rttAlpha);
            _jitterFilter = new EwmaFilter(jitterAlpha);
            _lastRttMs = 0;
        }

        public double SmoothedRtt => _rttFilter.Value;
        public double Jitter => _jitterFilter.Value;

        public void AddSample(double rttMs)
        {
            _rttFilter.Update(rttMs);

            if (_lastRttMs > 0)
            {
                var delta = Math.Abs(rttMs - _lastRttMs);
                _jitterFilter.Update(delta);
            }
            
            _lastRttMs = rttMs;
        }
    }
}
