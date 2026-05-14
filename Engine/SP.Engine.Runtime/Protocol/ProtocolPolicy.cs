using System.Collections.Generic;
using System.Net.Sockets;

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

    public interface IPolicySnapshot
    {
        ProtocolPolicy Globals { get; }
        ProtocolPolicy Resolve(ushort protocolId);
    }

    public sealed class PolicySnapshot : IPolicySnapshot
    {
        private const int MinCompressionThreshold = 128;
        private readonly ProtocolPolicy[] _cache = new ProtocolPolicy[ushort.MaxValue];

        public ProtocolPolicy Globals { get; }

        public PolicySnapshot(PolicyGlobals g, Dictionary<ushort, ProtocolOverrides> overrides)
        {
            Globals = new ProtocolPolicy(g.UseEncrypt, g.UseCompress, g.CompressionThreshold);
            foreach (var (id, ov) in overrides)
            {
                _cache[id] = ComputePolicy(ov, g);
            }
        }
        
        private static ProtocolPolicy ComputePolicy(ProtocolOverrides ov, PolicyGlobals g)
        {
            // 글로벌 설정을 우선으로 함
            var useEncrypt = g.UseEncrypt && (ov.Encrypt == Toggle.Inherit || ov.Encrypt == Toggle.On);
            var useCompress = g.UseCompress && (ov.Encrypt == Toggle.Inherit || ov.Compress == Toggle.On);

            if (!useCompress)
                return new ProtocolPolicy(useEncrypt, false, 0);

            var threshold = g.CompressionThreshold < MinCompressionThreshold ? MinCompressionThreshold : g.CompressionThreshold;
            return new ProtocolPolicy(useEncrypt, true, threshold);
        }
        
        public ProtocolPolicy Resolve(ushort protocolId)
            => _cache[protocolId] ?? Globals;
    }
}
