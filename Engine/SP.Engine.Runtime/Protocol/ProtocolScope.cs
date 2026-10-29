using System.Runtime.CompilerServices;

namespace SP.Engine.Runtime.Protocol
{
    /// <summary>
    /// 고성능 스택 전용 프로토콜 스마트 포인터
    /// </summary>
    public ref struct ProtocolScope<T> where T : class, IProtocolData, new()
    {
        private readonly bool _pooled;

        public T Protocol { get; private set; }

        public ProtocolScope(T protocol, bool pooled)
        {
            Protocol = protocol;
            _pooled = pooled;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ProtocolScope<T> Rent()
        {
            return new ProtocolScope<T>(ProtocolPool<T>.Rent(), pooled: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            var protocol = Protocol;
            if (!_pooled || protocol == null) return;
            Protocol = null;
            ProtocolPool<T>.Return(protocol);
        }
    }
}
