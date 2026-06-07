using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Protocol;

internal static class ProtocolDispatcher
{
    private static readonly ConcurrentDictionary<Type, Func<PeerBase, IProtocolData, bool>> _invokerCache = new();

    public static bool DispatchSend(PeerBase peer, IProtocolData data)
    {
        if (data == null) return false;

        var invoker = _invokerCache.GetOrAdd(data.GetType(), CreateInvoker);
        return invoker(peer, data);
    }

    private static Func<PeerBase, IProtocolData, bool> CreateInvoker(Type type)
    {
        var peerParam = Expression.Parameter(typeof(PeerBase), "peer");
        var dataParam = Expression.Parameter(typeof(IProtocolData).MakeByRefType(), "data");
        
        var methodInfo = typeof(PeerBase)
            .GetMethod(nameof(PeerBase.Send), BindingFlags.Public | BindingFlags.Instance)
            ?.MakeGenericMethod(type);

        if (methodInfo == null)
        {
            throw new InvalidOperationException($"PeerBase.Send generic method missing for type: {type.Name}");
        }

        var castedData = Expression.Convert(dataParam, type);
        var call = Expression.Call(peerParam, methodInfo, castedData);
        return Expression.Lambda<Func<PeerBase, IProtocolData, bool>>(call, peerParam, dataParam).Compile();
    }
}
