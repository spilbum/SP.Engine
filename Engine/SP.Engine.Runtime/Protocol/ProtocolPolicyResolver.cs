using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace SP.Engine.Runtime.Protocol
{
    public static class ProtocolPolicyResolver
    {
        
        private const int MinThreshold = 128;
        private static readonly ConcurrentDictionary<Type, ProtocolOverrides> Overrides =
            new ConcurrentDictionary<Type, ProtocolOverrides>();

        private static ProtocolOverrides ReadOverrides(Type t)
        {
            var a = t.GetCustomAttribute<ProtocolAttribute>();
            if (a == null)
                throw new InvalidOperationException($"[{t.FullName}] requires {nameof(ProtocolAttribute)}.");
            var th = a.CompressionThreshold > 0 ? (int?)a.CompressionThreshold : null;
            return new ProtocolOverrides(a.Encrypt, a.Compress, th);
        }

        private static int ClampThreshold(int threshold)
        {
            if (threshold <= 0) return MinThreshold;
            return threshold < MinThreshold ? MinThreshold : threshold;
        }

        private static ProtocolPolicy Merge(in ProtocolOverrides ov, in PolicyGlobals g)
        {
            var useEncrypt = g.UseEncrypt && (ov.Encrypt == Toggle.Inherit || ov.Encrypt.ToBool());
            var useCompress = g.UseCompress && (ov.Compress == Toggle.Inherit || ov.Compress.ToBool());

            if (!useCompress)
                return new ProtocolPolicy(useEncrypt, false, 0);
            
            var th = ov.CompressionThreshold ?? g.CompressionThreshold;
            th = ClampThreshold(th);
            return new ProtocolPolicy(useEncrypt, true, th);
        }

        public static ProtocolPolicy Resolve(Type protocolType, in PolicyGlobals globals)
        {
            if (protocolType == null) throw new ArgumentNullException(nameof(protocolType));
            if (!typeof(IProtocolData).IsAssignableFrom(protocolType))
                throw new ArgumentException($"Type {protocolType.FullName} must implement IProtocol.", nameof(protocolType));

            var ov = Overrides.GetOrAdd(protocolType, ReadOverrides);
            return Merge(ov, globals);
        }

        public static ProtocolPolicy Resolve<TProtocol>(in PolicyGlobals globals) where TProtocol : IProtocolData
            => Resolve(typeof(TProtocol), globals);
    }
}
