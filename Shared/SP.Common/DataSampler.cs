using System;
using System.Collections.Generic;
using System.Linq;

namespace SP.Common
{
    public class DataSampler
    {
        private const int MinSamplesForOutlierCheck = 10;
        
        private readonly Queue<double> _samples;
        private readonly int _capacity;
        private readonly object _lock = new object();

        private double _sum;
        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        
        public bool EnableOutlierFilter { get; set; } = true;
        public double OutlierThreshold { get; set; } = 3.0;

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _samples.Count;
                }
            }
        }

        public double Avg
        {
            get
            {
                lock (_lock)
                {
                    return _samples.Count == 0 ? 0 : _sum / _samples.Count;
                }
            }
        }

        public double Min
        {
            get
            {
                lock (_lock)
                {
                    return _samples.Count == 0 ? 0 : _min;
                }
            }
        }

        public double Max
        {
            get
            {
                lock (_lock)
                {
                    return _samples.Count == 0 ? 0 : _max;
                }
            }
        }
        
        public double StdDev
        {
            get
            {
                lock (_lock)
                {
                    if (_samples.Count == 0) return 0.0;
                    var mean = Avg;
                    return Math.Sqrt(_samples.Average(v => Math.Pow(v - mean, 2)));
                }
            }
        }
        
        public double Jitter
        {
            get
            {
                lock (_lock)
                {
                    if (_samples.Count < 2) return 0.0;
                    var arr = _samples.ToArray();
                    double sumDelta = 0;
                    for (var i = 1; i < arr.Length; i++)
                        sumDelta += Math.Abs(arr[i] - arr[i - 1]);
                    return sumDelta / (arr.Length - 1);
                }
            }
        }
        
        public DataSampler(int capacity = 100)
        {
            if (capacity <= 0)
                throw new ArgumentException("Capacity must be greater than zero.", nameof(capacity));

            _capacity = capacity;
            _samples = new Queue<double>(capacity);
        }
        
        public void Add(double value)
        {
            lock (_lock)
            {
                if (EnableOutlierFilter && IsOutlier(value))
                    return;

                _samples.Enqueue(value);
                _sum += value;
                _min = Math.Min(_min, value);
                _max = Math.Max(_max, value);

                if (_samples.Count >= _capacity)
                {
                    var removed = _samples.Dequeue();
                    _sum -= removed;

                    if (NearlyEquals(removed, _min) || NearlyEquals(removed, _max))
                    {
                        var arr = _samples.ToArray();
                        _min = arr.Min();
                        _max = arr.Max();
                    }
                }
            }
        }

        private bool IsOutlier(double value)
        {
            if (_samples.Count < MinSamplesForOutlierCheck)
                return false;
            
            var mean = Avg;
            var stdDev = StdDev;
            var lower = Math.Max(0, mean - OutlierThreshold * stdDev);
            var upper = mean + OutlierThreshold * stdDev;
            return value < lower || value > upper;
        }

        public void Clear()
        {
            lock (_lock)
            {
                _samples.Clear();
                _sum = 0;
                _min = double.MaxValue;
                _max = double.MinValue;
            }
        }
        
        
        public double[] ToArray()
        {
            lock (_lock)
            {
                return _samples.ToArray();
            }
        }

        private static bool NearlyEquals(double a, double b, double epsilon = 0.01)
            => Math.Abs(a - b) < epsilon;
    }
}
