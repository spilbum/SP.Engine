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
        public const ushort UdpHealthCheckConfirm = 105;
    }

    public static class S2CEngineProtocolId
    {
        public const ushort SessionAuthAck = 200;
        public const ushort MessageAck = 201;
        public const ushort Pong = 202;
        public const ushort Close = 203;
        public const ushort UdpHelloAck = 204;
        public const ushort UdpHealthCheck = 205;
        public const ushort UdpStatusNotify = 206;
    }

    public static class C2SEngineProtocolData
    {
        [Protocol(C2SEngineProtocolId.SessionAuthReq)]
        public class SessionAuthReq : ProtocolDataBase<SessionAuthReq>
        {
            public byte[]? ClientPublicKey;
            public DhKeySize KeySize;
            public uint PeerId;
            public long SessionId;
        }

        [Protocol(C2SEngineProtocolId.MessageAck)]
        public class MessageAck : ProtocolDataBase<MessageAck>
        {
            public uint AckNumber;
        }

        [Protocol(C2SEngineProtocolId.Ping)]
        public class Ping : ProtocolDataBase<Ping>
        {
            public uint SendTimeMs;
            // 최신 RTT
            public double RawRttMs;
            // EWMA 평균 RTT
            public double AvgRttMs;
            // EWMA 지터
            public double JitterMs;
        }

        [Protocol(C2SEngineProtocolId.Close)]
        public class Close : ProtocolDataBase<Close>
        {
        }

        [Protocol(C2SEngineProtocolId.UdpHelloReq, ChannelKind.Unreliable)]
        public class UdpHelloReq : ProtocolDataBase<UdpHelloReq>
        {
            public ushort Mtu;
            public uint PeerId;
            public long SessionId;
        }

        [Protocol(C2SEngineProtocolId.UdpHealthCheckConfirm, ChannelKind.Unreliable)]
        public class UdpHealthCheckConfirm : ProtocolDataBase<UdpHealthCheckConfirm>
        {
        }
    }

    public static class S2CEngineProtocolData
    {
        [Protocol(S2CEngineProtocolId.SessionAuthAck)]
        public class SessionAuthAck : ProtocolDataBase<SessionAuthAck>
        {
            public int CompressionThreshold;
            public int MaxFrameBytes;
            public int MaxRetries;
            public uint PeerId;
            public SessionAuthResult Result;
            public int SendTimeoutMs;
            public byte[]? ServerPublicKey;
            public long SessionId;
            public bool UseCompress;
            public bool UseEncrypt;
            public int MaxAckDelayMs;
            public int AckStepThreshold;
            public int UdpOpenPort;
            public int UdpAssemblyTimeoutSec;
            public int UdpMaxPendingMessageCount;
            public int UdpCleanupIntervalSec;
            public int MaxOutOfOrderCount;
        }

        [Protocol(S2CEngineProtocolId.Pong)]
        public class Pong : ProtocolDataBase<Pong>
        {
            // 클라이언트가 핑 보낸 시간
            public uint ClientSendTimeMs;
            // 서버측 현재 시간
            public uint ServerTimeMs;
        }

        [Protocol(S2CEngineProtocolId.MessageAck)]
        public class MessageAck : ProtocolDataBase<MessageAck>
        {
            public uint AckNumber;
        }

        [Protocol(S2CEngineProtocolId.Close)]
        public class Close : ProtocolDataBase<Close>
        {
        }

        [Protocol(S2CEngineProtocolId.UdpHelloAck, ChannelKind.Unreliable)]
        public class UdpHelloAck : ProtocolDataBase<UdpHelloAck>
        {
            public ushort Mtu;
            public UdpHandshakeResult Result;
        }
        
        [Protocol(S2CEngineProtocolId.UdpHealthCheck, ChannelKind.Unreliable)]
        public class UdpHealthCheck : ProtocolDataBase<UdpHealthCheck>
        {
        }
        
        [Protocol(S2CEngineProtocolId.UdpStatusNotify)]
        public class UdpStatusNotify : ProtocolDataBase<UdpStatusNotify>
        {
            public bool IsEnabled;
        }
    }
}
