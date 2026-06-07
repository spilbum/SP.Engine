using System.Runtime.CompilerServices;
using SP.Core;
using SP.Core.Logging;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Protocol;

/// <summary>
/// 고성능 스택 전용 프로토콜 스마트 포인터
/// </summary>
public readonly ref struct ProtocolScope<T>(T protocol, bool isPooled) where T : class, IProtocolData, new()
{
    public readonly T Protocol = protocol;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ProtocolScope<T> Rent(ILogger logger = null)
    {
        using var _ = new SlowChecker(50, "new ProtocolScope", logger);
        return new ProtocolScope<T>(ProtocolPool<T>.Rent(), isPooled: true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (isPooled && Protocol != null)
        {
            ProtocolPool<T>.Return(Protocol);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator T(ProtocolScope<T> scope) => scope.Protocol;
}
