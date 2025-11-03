using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Protocol
{
    public static class C2SEngineProtocolId
    {
        public const ushort SessionAuthReq = 100;
        public const ushort MessageAck = 101;
        public const ushort Ping = 102;
        public const ushort Close = 103;
        public const ushort UdpHelloReq = 104;
        public const ushort UdpKeepAlive = 105;
    }

    public static class S2CEngineProtocolId
    {
        public const ushort SessionAuthAck = 200;
        public const ushort MessageAck = 201;
        public const ushort Pong = 202;
        public const ushort Close = 203;
        public const ushort UdpHelloAck = 204;
    }

    public static class C2SEngineProtocolData
    {
        [Protocol(C2SEngineProtocolId.SessionAuthReq, encrypt: Toggle.Off, compress: Toggle.Off)]
        public class SessionAuthReq : BaseProtocolData
        {
            public byte[]? ClientPublicKey;
            public DhKeySize KeySize;
            public uint PeerId;
            public string? SessionId;
        }

        [Protocol(C2SEngineProtocolId.MessageAck)]
        public class MessageAck : BaseProtocolData
        {
            public long SequenceNumber;
        }

        [Protocol(C2SEngineProtocolId.Ping)]
        public class Ping : BaseProtocolData
        {
            public double AvgRttMs;
            public double JitterMs;
            public float PacketLossRate;
            public double RawRttMs;
            public long SendTimeMs;
        }

        [Protocol(C2SEngineProtocolId.Close)]
        public class Close : BaseProtocolData
        {
        }

        [Protocol(C2SEngineProtocolId.UdpHelloReq, ChannelKind.Unreliable)]
        public class UdpHelloReq : BaseProtocolData
        {
            public ushort Mtu;
            public uint PeerId;
            public string? SessionId;
        }

        [Protocol(C2SEngineProtocolId.UdpKeepAlive, ChannelKind.Unreliable)]
        public class UdpKeepAlive : BaseProtocolData
        {
        }
    }

    public static class S2CEngineProtocolData
    {
        [Protocol(S2CEngineProtocolId.SessionAuthAck, encrypt: Toggle.Off, compress: Toggle.Off)]
        public class SessionAuthAck : BaseProtocolData
        {
            public int CompressionThreshold;
            public int MaxFrameBytes;
            public int MaxRetryCount;
            public uint PeerId;
            public string? Reason;
            public SessionHandshakeResult Result;
            public int SendTimeoutMs;
            public byte[]? ServerPublicKey;
            public string? SessionId;
            public int UdpOpenPort;
            public bool UseCompress;
            public bool UseEncrypt;
        }

        [Protocol(S2CEngineProtocolId.Pong)]
        public class Pong : BaseProtocolData
        {
            public long SendTimeMs;
            public long ServerTimeMs;
        }

        [Protocol(S2CEngineProtocolId.MessageAck)]
        public class MessageAck : BaseProtocolData
        {
            public long SequenceNumber;
        }

        [Protocol(S2CEngineProtocolId.Close)]
        public class Close : BaseProtocolData
        {
        }

        [Protocol(S2CEngineProtocolId.UdpHelloAck, ChannelKind.Unreliable)]
        public class UdpHelloAck : BaseProtocolData
        {
            public ushort Mtu;
            public UdpHandshakeResult Result;
        }
    }
}
