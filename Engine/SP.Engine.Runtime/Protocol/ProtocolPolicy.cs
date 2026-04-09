using System;
using System.Collections.Generic;

namespace SP.Engine.Runtime.Protocol
{
    public interface IPolicy
    {
        bool UseEncrypt { get; }
        bool UseCompress { get; }
        int CompressionThreshold { get; }
    }

    public sealed class ProtocolPolicy : IPolicy
    {
        public ProtocolPolicy(bool useEncrypt, bool useCompress, int compressionThreshold)
        {
            UseEncrypt = useEncrypt;
            UseCompress = useCompress;
            CompressionThreshold = compressionThreshold;
        }

        public bool UseEncrypt { get; }
        public bool UseCompress { get; }
        public int CompressionThreshold { get; }
    }

    public struct PolicyGlobals
    {
        public readonly bool UseEncrypt;
        public readonly bool UseCompress;
        public readonly int CompressionThreshold;

        public PolicyGlobals(bool useEncrypt, bool useCompress, int compressionThreshold)
        {
            UseEncrypt = useEncrypt;
            UseCompress = useCompress;
            CompressionThreshold = compressionThreshold;
        }
    }

    public struct ProtocolOverrides
    {
        public readonly Toggle Encrypt;
        public readonly Toggle Compress;

        public ProtocolOverrides(Toggle encrypt, Toggle compress)
        {
            Encrypt = encrypt;
            Compress = compress;
        }
    }

    public interface IProtocolPolicySnapshot
    {
        ProtocolPolicy Resolve(ushort protocolId);
    }

    public sealed class PolicySnapshot : IProtocolPolicySnapshot
    {
        private readonly ProtocolPolicy[] _cache = new ProtocolPolicy[ushort.MaxValue];
        private readonly ProtocolPolicy _fallbackPolicy;

        public PolicySnapshot(PolicyGlobals globals, Dictionary<ushort, ProtocolOverrides> overrides)
        {
            _fallbackPolicy = new ProtocolPolicy(globals.UseEncrypt, globals.UseCompress, globals.CompressionThreshold);
            
            foreach (var (id, ov) in overrides)
            {
                _cache[id] = ProtocolPolicyRegistry.ComputePolicy(ov, globals);
            }
        }
        
        public ProtocolPolicy Resolve(ushort protocolId)
            => _cache[protocolId] ?? _fallbackPolicy;
    }

    public static class PolicyDefaults
    {
        // 내부 프로토콜용 정책
        public static readonly ProtocolPolicy InternalPolicy = new ProtocolPolicy(false, false, 0);
        // 설정된 정책이 없을때 적용되는 기본 정책
        public static readonly PolicyGlobals FallbackGlobals = new PolicyGlobals(true, false, 0);
    }
}
