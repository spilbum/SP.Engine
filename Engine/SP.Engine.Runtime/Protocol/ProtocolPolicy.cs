using System;

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

    public interface IPolicyView
    {
        ProtocolPolicy Resolve<TProtocol>() where TProtocol : IProtocolData;
        ProtocolPolicy Resolve(Type protocolType);
    }

    public sealed class NetworkPolicyView : IPolicyView
    {
        private readonly PolicyGlobals _globals;

        public NetworkPolicyView(in PolicyGlobals globals)
        {
            _globals = globals;
        }

        public ProtocolPolicy Resolve<TProtocol>() where TProtocol : IProtocolData
        {
            return ProtocolPolicyResolver.Resolve<TProtocol>(_globals);
        }

        public ProtocolPolicy Resolve(Type protocolType)
        {
            return ProtocolPolicyResolver.Resolve(protocolType, _globals);
        }
    }

    public static class PolicyDefaults
    {
        public static readonly PolicyGlobals Globals = new PolicyGlobals(false, false, 0);
    }
}
