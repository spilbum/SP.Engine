using System;
using System.Runtime.CompilerServices;

namespace SP.Engine.Runtime.Protocol
{
    internal static class ProtocolPool<T> where T : class, IProtocolData, new()
    {
        [ThreadStatic] private static T _cachedInstance;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Rent()
        {
            var instance = _cachedInstance;
            if (instance == null) return new T();
            _cachedInstance = null;
            return instance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(T protocol)
        {
            if (protocol == null) return;

            try
            {
                protocol.Reset();
            }
            catch
            {
                return;
            }

            _cachedInstance ??= protocol;
        }
    }
}
