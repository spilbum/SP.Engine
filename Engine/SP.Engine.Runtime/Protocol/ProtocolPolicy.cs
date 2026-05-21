using System.Collections.Generic;

namespace SP.Engine.Runtime.Protocol
{
    public interface IPolicy
    {
        bool UseEncrypt { get; }
        bool UseCompress { get; }
        int CompressionThreshold { get; }
        int MaxPayloadLength { get; }
    }

    public sealed class ProtocolPolicy : IPolicy
    {
        public ProtocolPolicy(bool useEncrypt, bool useCompress, int compressionThreshold, int maxPayloadLength)
        {
            UseEncrypt = useEncrypt;
            UseCompress = useCompress;
            CompressionThreshold = compressionThreshold;
            MaxPayloadLength = maxPayloadLength;
        }

        public bool UseEncrypt { get; }
        public bool UseCompress { get; }
        public int CompressionThreshold { get; }
        public int MaxPayloadLength { get; }
    }

    public struct PolicyGlobals
    {
        public readonly bool UseEncrypt;
        public readonly bool UseCompress;
        public readonly int CompressionThreshold;
        public readonly int MaxPayloadLength;

        public PolicyGlobals(bool useEncrypt, bool useCompress, int compressionThreshold, int maxPayloadLength)
        {
            UseEncrypt = useEncrypt;
            UseCompress = useCompress;
            CompressionThreshold = compressionThreshold;
            MaxPayloadLength = maxPayloadLength;
        }
    }

    public struct ProtocolOverrides
    {
        public readonly Toggle Encrypt;
        public readonly Toggle Compress;
        public readonly int MaxPayloadLength;

        public ProtocolOverrides(Toggle encrypt, Toggle compress, int maxPayloadLength)
        {
            Encrypt = encrypt;
            Compress = compress;
            MaxPayloadLength = maxPayloadLength;
        }
    }

    public interface IPolicySnapshot
    {
        ProtocolPolicy Resolve(ushort protocolId);
    }

    public sealed class PolicySnapshot : IPolicySnapshot
    {
        private const int MinCompressionThreshold = 128;
        private const int MinPayloadLength = 64;
        private readonly ProtocolPolicy[] _cache = new ProtocolPolicy[ushort.MaxValue];

        public ProtocolPolicy Globals { get; }

        public PolicySnapshot(PolicyGlobals g, Dictionary<ushort, ProtocolOverrides> overrides)
        {
            var maxPayloadLength = g.MaxPayloadLength < MinPayloadLength ? MinPayloadLength : g.MaxPayloadLength;
            Globals = new ProtocolPolicy(g.UseEncrypt, g.UseCompress, g.CompressionThreshold, maxPayloadLength);
            
            foreach (var (id, ov) in overrides)
            {
                _cache[id] = ComputePolicy(ov, g);
            }
        }
        
        private static ProtocolPolicy ComputePolicy(ProtocolOverrides ov, PolicyGlobals g)
        {
            // 글로벌 설정을 우선으로 함
            var useEncrypt = g.UseEncrypt && (ov.Encrypt == Toggle.Inherit || ov.Encrypt == Toggle.On);
            var useCompress = g.UseCompress && (ov.Compress == Toggle.Inherit || ov.Compress == Toggle.On);

            var length = ov.MaxPayloadLength == -1 ? g.MaxPayloadLength : ov.MaxPayloadLength;
            var maxPayloadLength = length < MinPayloadLength ? MinPayloadLength : length;
            
            if (!useCompress)
                return new ProtocolPolicy(useEncrypt, false, 0, maxPayloadLength);

            var compressionThreshold = g.CompressionThreshold < MinCompressionThreshold ? MinCompressionThreshold : g.CompressionThreshold;
            return new ProtocolPolicy(useEncrypt, true, compressionThreshold, maxPayloadLength);
        }
        
        public ProtocolPolicy Resolve(ushort protocolId)
            => _cache[protocolId] ?? Globals;
    }
}
