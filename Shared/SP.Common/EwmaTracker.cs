using System;

namespace SP.Common
{
    /// <summary>
    /// EWMA (Expoential Weighted Moving Average) 기반 예측기
    /// </summary>
    public class EwmaTracker
    {
        /// <summary>
        /// 최신 값에 반영할 가중치 계수
        /// </summary>
        public double Alpha { get; }
        
        /// <summary>
        /// 현재까지의 추정값
        /// </summary>
        public double Estimated { get; private set; }
        
        /// <summary>
        /// 초기화 여부
        /// </summary>
        public bool IsInitialized { get; private set; }

        public EwmaTracker(double alpha)
        {
            if (alpha <= 0 || alpha > 1)
                throw new ArgumentOutOfRangeException(nameof(alpha), "Alpha must be between (0, 1].");
            Alpha = alpha;
        }

        /// <summary>
        /// 새로운 측정값을 반영하여 예측값을 업데이트합니다.
        /// </summary>
        /// <param name="newValue">측정된 원시 값</param>
        public void Update(double newValue)
        {
            if (!IsInitialized)
            {
                Estimated = newValue;
                IsInitialized = true;
            }
            else
            {
                Estimated = Math.Round((1 - Alpha) * Estimated + Alpha * newValue, 2);
            }
        }

        /// <summary>
        /// 예측값을 초기화합니다.
        /// </summary>
        /// <param name="initialValue">초기값</param>
        public void Reset(double initialValue)
        {
            Estimated = initialValue;
            IsInitialized = false;
        }

        /// <summary>
        /// 예측기를 초기 상태로 리셋합니다.
        /// </summary>
        public void Clear()
        {
            Estimated = 0;
            IsInitialized = false;
        }

        public override string ToString()
        {
            return IsInitialized ? Estimated.ToString("F2") : "(uninitialized)";
        }
    }
}
