using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Policy
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

 
}
