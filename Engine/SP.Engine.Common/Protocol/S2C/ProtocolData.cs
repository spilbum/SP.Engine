using SP.Engine.Runtime;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Common.Protocol.S2C
{
    [ProtocolData(ProtocolId.S2C.SessionAuthAck)]
    internal class SessionAuthAck : ProtocolDataBase<SessionAuthAck>
    {
        public SessionAuthResult Result;
        public long SessionId;
        public uint PeerId;
        public byte[]? EncryptPublicKey;
        public uint NextExpectedSeq;
        public int MaxPayloadLength;
        public int CompressionThreshold;
        public bool UseCompress;
        public bool UseEncrypt;
        public int UdpOpenPort;
        public int FragmentAssemblerCleanupTimeoutSec;
        public int FragmentAssemblerPendingMessageThreshold;
        public int FragmentAssemblerCleanupIntervalSec;
        public int ReliableMaxOutOfOrderCount;
        public int ReliablePendingQueueCapacity;
        public int ReliableInFlightLimit;
        public int ReliableMaxAckDelayMs;
        public int ReliableAckFrequency;
        public int ReliableMaxRetransmitCount;
        public int ReliableInitialRetransmitTimeoutMs;
    }

    [ProtocolData(ProtocolId.S2C.MessageAck)]
    internal class MessageAck : ProtocolDataBase<MessageAck>
    {
        public uint NextExpectedSeq;
    }

    [ProtocolData(ProtocolId.S2C.Pong)]
    internal class Pong : ProtocolDataBase<Pong>
    {
        // 클라이언트가 핑 보낸 시간
        public uint SentTimeMs;
        // 서버측 현재 시간
        public uint ServerTimeMs;
    }

    [ProtocolData(ProtocolId.S2C.CloseCmd)]
    internal class CloseCmd : ProtocolDataBase<CloseCmd>
    {
        
    }

    [ProtocolData(ProtocolId.S2C.UdpHelloAck)]
    internal class UdpHelloAck : ProtocolDataBase<UdpHelloAck>
    {
        public UdpHelloResult Result;
        public ushort Mtu;
    }

    [ProtocolData(ProtocolId.S2C.UdpHealthCheckReq)]
    internal class UdpHealthCheckReq : ProtocolDataBase<UdpHealthCheckReq>
    {
        
    }

    [ProtocolData(ProtocolId.S2C.UdpStatusNotify)]
    internal class UdpStatusNotify : ProtocolDataBase<UdpStatusNotify>
    {
        public bool IsEnabled;
    }
}
