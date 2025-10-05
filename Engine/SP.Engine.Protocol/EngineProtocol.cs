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
        public class SessionAuthReq : BaseProtocol
        {
            public string? SessionId;
            public uint PeerId;
            public DhKeySize KeySize;
            public byte[]? ClientPublicKey;
        }
            
        [Protocol(C2SEngineProtocolId.MessageAck)]
        public class MessageAck : BaseProtocol
        {
            public long SequenceNumber;
        }
            
        [Protocol(C2SEngineProtocolId.Ping)]
        public class Ping : BaseProtocol
        {
            public double RawRttMs;
            public double AvgRttMs;
            public double JitterMs;
            public float PacketLossRate;
            public long SendTimeMs;
        }
            
        [Protocol(C2SEngineProtocolId.Close)]
        public class Close : BaseProtocol
        {
        }

        [Protocol(C2SEngineProtocolId.UdpHelloReq, ChannelKind.Unreliable)]
        public class UdpHelloReq : BaseProtocol
        {
            public string? SessionId;
            public uint PeerId;
            public ushort Mtu;
        }

        [Protocol(C2SEngineProtocolId.UdpKeepAlive, ChannelKind.Unreliable)]
        public class UdpKeepAlive : BaseProtocol
        {
        }
    }

    public static class S2CEngineProtocolData
    {
        [Protocol(S2CEngineProtocolId.SessionAuthAck, encrypt: Toggle.Off, compress: Toggle.Off)]
        public class SessionAuthAck : BaseProtocol
        {
            public SessionHandshakeResult Result;
            public string? SessionId;
            public int MaxFrameBytes;
            public int SendTimeoutMs;
            public int MaxRetryCount;
            public int UdpOpenPort;
            public uint PeerId;
            public bool UseEncrypt;
            public byte[]? ServerPublicKey;
            public bool UseCompress;
            public int CompressionThreshold;
            public string? Reason;
        }

        [Protocol(S2CEngineProtocolId.Pong)]
        public class Pong : BaseProtocol
        {
            public long SendTimeMs;
            public long ServerTimeMs;
        }

        [Protocol(S2CEngineProtocolId.MessageAck)]
        public class MessageAck : BaseProtocol
        {
            public long SequenceNumber;
        }

        [Protocol(S2CEngineProtocolId.Close)]
        public class Close : BaseProtocol
        {
        }

        [Protocol(S2CEngineProtocolId.UdpHelloAck, ChannelKind.Unreliable)]
        public class UdpHelloAck : BaseProtocol
        {
            public UdpHandshakeResult Result;
            public ushort Mtu;
        }
    }
}
