using System;

namespace SP.Common
{
    /// <summary>
    /// EWMA (Expoential Weighted Moving Average) 기반 예측기
    /// </summary>
    public class EwmaTracker
    {
        /// <summary>
        /// 최신 값에 반영할 가중치 계수 (0 ~ 1)
        /// 값이 클수록 최신 값 반영이 빠름
        /// 값이 작을수록 변화 반응이 느림
        /// </summary>
        private double _alpha;
        
        public double Estimated { get; private set; }
        public bool IsInitialized { get; private set; }

        public EwmaTracker(double alpha)
        {
            if (alpha <= 0 || alpha > 1)
                throw new ArgumentOutOfRangeException(nameof(alpha), "Alpha must be between (0, 1].");
            _alpha = alpha;
        }
        
        public void Update(double value)
        {
            if (!IsInitialized)
            {
                Estimated = value;
                IsInitialized = true;
            }
            else
            {
                Estimated = (1 - _alpha) * Estimated + _alpha * value;
            }
        }
        
        public void Initialize(double initialValue)
        {
            Estimated = initialValue;
            IsInitialized = true;
        }
        
        public void Clear()
        {
            Estimated = 0;
            IsInitialized = false;
        }
    }
}
