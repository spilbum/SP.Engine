using System;

namespace SP.Core
{
    public class Singleton<T> where T : Singleton<T>
    {
        private static readonly Lazy<T> InstanceLazy = new Lazy<T>(CreateInstance);

        /// <summary>
        ///     하위 클래스에서만 호출 가능하도록 protected 생성자 추가
        /// </summary>
        protected Singleton()
        {
        }

        /// <summary>
        ///     싱글톤 인스턴스 반환
        /// </summary>
        public static T Instance => InstanceLazy.Value;

        /// <summary>
        ///     인스턴스를 생성하는 메서드
        /// </summary>
        private static T CreateInstance()
        {
            return Activator.CreateInstance(typeof(T), true) as T
                   ?? throw new InvalidOperationException($"Cannot create an instance of {typeof(T)}.");
        }
    }
}
