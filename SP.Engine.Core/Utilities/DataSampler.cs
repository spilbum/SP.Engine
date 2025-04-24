using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SP.Engine.Core.Utilities
{
    /// <summary>
    /// 샘플링 데이터를 관리하는 제네릭 클래스 (숫자 전용)
    /// </summary>
    public class DataSampler<T> where T : struct, IConvertible
    {
        private readonly ConcurrentQueue<T> _sample = new ConcurrentQueue<T>();
        private readonly int _capacity;
        private readonly object _lock = new object();

        /// <summary>
        /// 기본 표본 크기
        /// </summary>
        private const int DefaultCapacity = 100;
        /// <summary>
        /// 이상치 필터 활성화 여부
        /// </summary>
        /// <value></value>
        public bool EnableOutlierFilter { get; set; } = true;
        /// <summary>
        /// 이상치 감지 임계값
        /// </summary>
        public double OutlierThreshold { get; set; } = 3.0;

        /// <summary>
        /// 첫 번째 값 반환 (없으면 default)
        /// </summary>
        public T FirstValue
        {
            get
            {
                lock (_lock)
                {
                    return _sample.TryPeek(out var value) ? value : default;
                }
            }
        }

        /// <summary>
        /// 마지막 값 저장
        /// </summary>
        public T LastValue { get; private set; }

        /// <summary>
        /// 평균 값 반환 (샘플 데이터가 없으면 0.0)
        /// </summary>
        public double Avg
        {
            get
            {
                lock (_lock)
                {
                    if (_sample.Count == 0) return 0.0;
                    return _sample.Average(v => Convert.ToDouble(v));
                }
            }
        }

        /// <summary>
        /// 표준 편차 계산 (샘플 데이터가 없으면 0.0)
        /// </summary>
        public double StdDev
        {
            get
            {
                lock (_lock)
                {
                    if (_sample.Count == 0) return 0.0;
                    double mean = Avg;
                    double variance = _sample.Average(v => Math.Pow(Convert.ToDouble(v) - mean, 2));
                    return Math.Sqrt(variance);
                }
            }
        }

        /// <summary>
        /// 생성자: 표본 크기 지정 가능
        /// </summary>
        public DataSampler(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
                throw new ArgumentException("Capacity must be greater than zero.", nameof(capacity));

            _capacity = capacity;
        }

        /// <summary>
        /// 데이터 추가 (동기)
        /// </summary>
        public void Add(T value)
        {
            lock (_lock)
            {
                var doubleValue = Convert.ToDouble(value);
                if (EnableOutlierFilter && _sample.Count > 10)
                {
                    var mean = Avg;
                    var stdDev = StdDev;
                    var lowerBound = mean - (OutlierThreshold * stdDev);
                    var upperBound = mean + (OutlierThreshold * stdDev);

                    if (doubleValue < lowerBound || doubleValue > upperBound)
                    {
                        Console.WriteLine($"[Warning] Outlier detected: {doubleValue} (Threshold: {lowerBound} ~ {upperBound})");
                        return;
                    }
                }

                _sample.Enqueue(value);
                LastValue = value;

                // 초과된 경우 제거
                while (_sample.Count > _capacity && _sample.TryDequeue(out _)) 
                { 
                    //noting... 
                }
            }
        }

        /// <summary>
        /// 데이터 추가 (비동기)
        /// </summary>
        public async Task AddAsync(T value)
        {
            await Task.Run(() => Add(value));
        }

        /// <summary>
        /// 데이터 리스트로 변환
        /// </summary>
        public List<T> ToList()
        {
            lock (_lock)
            {
                return _sample.ToList();
            }
        }

        /// <summary>
        /// 전체 데이터 배열 반환
        /// </summary>
        public T[] ToArray()
        {
            lock (_lock)
            {
                return _sample.ToArray();
            }
        }

        /// <summary>
        /// 표본 데이터 개수 반환
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _sample.Count;
                }
            }
        }

        /// <summary>
        /// 표본 데이터 초기화
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _sample.Clear();
            }
        }
    }
}
