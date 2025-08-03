using System;

namespace SP.Common
{
    /// <summary>
    /// EWMA (Expoential Weighted Moving Average) 기반 예측기
    /// </summary>
    public class EwmaFilter
    {
        /// <summary>
        /// 최신 값에 반영할 가중치 계수 (0 ~ 1)
        /// 값이 클수록 최신 값 반영이 빠름
        /// 값이 작을수록 변화 반응이 느림
        /// </summary>
        private readonly double _alpha;

        public bool IsInitialized { get; private set; }
        public double Value { get; private set; }

        public EwmaFilter(double alpha)
        {
            if (alpha <= 0 || alpha > 1)
                throw new ArgumentOutOfRangeException(nameof(alpha), "Alpha must be between (0, 1].");
            _alpha = alpha;
        }
        
        public void Update(double value)
        {
            if (!IsInitialized)
            {
                Value = value;
                IsInitialized = true;
            }
            else
            {
                Value = (1 - _alpha) * Value + _alpha * value;
            }
        }

        public void Reset() => IsInitialized = false;
    }
}
