using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SP.Engine.Runtime.Protocol
{
    public interface IProtocolPolicyProvider
    {
        ProtocolPolicy Resolve(ushort protocolId);
    }

    public sealed class ProtocolPolicyProvider : IProtocolPolicyProvider
    {
        private const int MinCompressionThreshold = 128;
        private readonly ProtocolPolicy[] _cache = new ProtocolPolicy[65536];

        public ProtocolPolicyProvider(GlobalProtocolPolicy g, Dictionary<ushort, ProtocolPolicyOverrides> overrides)
        { 
            var fallback = new ProtocolPolicy(g.UseEncrypt, g.UseCompress, g.CompressionThreshold);
            _cache.AsSpan().Fill(fallback);
            
            foreach (var (id, ov) in overrides)
            {
                _cache[id] = ComputePolicy(ov, g);
            }
        }
        
        private static ProtocolPolicy ComputePolicy(ProtocolPolicyOverrides o, GlobalProtocolPolicy g)
        {
            // 글로벌 설정을 우선으로 함
            var useEncrypt = g.UseEncrypt && o.Encrypt != Toggle.Off;
            var useCompress = g.UseCompress && o.Compress != Toggle.Off;

            if (!useCompress)
                return new ProtocolPolicy(useEncrypt, false, 0);

            var threshold = g.CompressionThreshold < MinCompressionThreshold 
                ? MinCompressionThreshold 
                : g.CompressionThreshold;
            
            return new ProtocolPolicy(useEncrypt, true, threshold);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ProtocolPolicy Resolve(ushort protocolId) => _cache[protocolId];
    }
}
