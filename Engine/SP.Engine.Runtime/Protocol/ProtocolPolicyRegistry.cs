using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SP.Engine.Runtime.Protocol
{
    public static class ProtocolPolicyRegistry
    {
        private const int MinThreshold = 128;
        private static readonly Dictionary<ushort, ProtocolOverrides> _overrides = new Dictionary<ushort, ProtocolOverrides>();

        public static void Initialize()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => typeof(IProtocolData).IsAssignableFrom(t) && !t.IsInterface);
            
            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<ProtocolAttribute>();
                if (attr == null) continue;
                _overrides[attr.Id] = new ProtocolOverrides(attr.Encrypt, attr.Compress);
            }
        }

        public static IProtocolPolicySnapshot CreateSnapshot(PolicyGlobals globals)
            => new PolicySnapshot(globals, _overrides);
        
        internal static ProtocolPolicy ComputePolicy(ProtocolOverrides ov, PolicyGlobals g)
        {
            // 글로벌 설정을 우선으로 함
            var useEncrypt = g.UseEncrypt && (ov.Encrypt == Toggle.Inherit || ov.Encrypt == Toggle.On);
            var useCompress = g.UseCompress && (ov.Encrypt == Toggle.Inherit || ov.Compress == Toggle.On);

            if (!useCompress)
                return new ProtocolPolicy(useEncrypt, false, 0);

            var threshold = g.CompressionThreshold < MinThreshold ? MinThreshold : g.CompressionThreshold;
            return new ProtocolPolicy(useEncrypt, true, threshold);
        }
    }
}
