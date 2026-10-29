
namespace SP.Engine.Runtime.Protocol
{
    public interface IProtocolPolicy
    {
        bool UseEncrypt { get; }
        bool UseCompress { get; }
        int CompressionThreshold { get; }
    }

    public readonly struct ProtocolPolicy : IProtocolPolicy
    {
        public readonly bool UseEncrypt;
        public readonly bool UseCompress;
        public readonly int CompressionThreshold;
        
        public ProtocolPolicy(bool useEncrypt, bool useCompress, int compressionThreshold)
        {
            UseEncrypt = useEncrypt;
            UseCompress = useCompress;
            CompressionThreshold = compressionThreshold;
        }

        bool IProtocolPolicy.UseEncrypt => UseEncrypt;
        bool IProtocolPolicy.UseCompress => UseCompress;
        int IProtocolPolicy.CompressionThreshold => CompressionThreshold;
    }

    public struct GlobalProtocolPolicy
    {
        public readonly bool UseEncrypt;
        public readonly bool UseCompress;
        public readonly int CompressionThreshold;

        public GlobalProtocolPolicy(bool useEncrypt, bool useCompress, int compressionThreshold)
        {
            UseEncrypt = useEncrypt;
            UseCompress = useCompress;
            CompressionThreshold = compressionThreshold;
        }
    }

    public struct ProtocolPolicyOverrides
    {
        public readonly Toggle Encrypt;
        public readonly Toggle Compress;
    
        public ProtocolPolicyOverrides(Toggle encrypt, Toggle compress)
        {
            Encrypt = encrypt;
            Compress = compress;
        }
    }
}
