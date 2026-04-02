using System;
using SP.Core;

namespace SP.Engine.Client
{
    public struct LatencyEstimator
    {
        private EwmaFilter _rttFilter;
        private EwmaFilter _jitterFilter;
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
            var wasInitialized = _rttFilter.IsInitialized;
            _rttFilter.Update(rttMs);

            if (wasInitialized)
            {
                var delta = Math.Abs(rttMs - _lastRttMs);
                _jitterFilter.Update(delta);
            }
            
            _lastRttMs = rttMs;
        }
    }
}
