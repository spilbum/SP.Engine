using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Common.Protocol.C2S
{
    [ProtocolData(ProtocolId.C2S.SessionAuthReq)]
    internal class SessionAuthReq : ProtocolDataBase<SessionAuthReq>
    {
        public byte[]? EncryptPublicKey;
        public DhKeySize EncryptKeySize;
        public PeerKind PeerKind;
        public uint NextExpectedSeq;
        public uint PeerId;
        public long SessionId;
    }

    [ProtocolData(ProtocolId.C2S.MessageAck)]
    internal class MessageAck : ProtocolDataBase<MessageAck>
    {
        public uint NextExpectedSeq;  
    }

    [ProtocolData(ProtocolId.C2S.Ping)]
    internal class Ping : ProtocolDataBase<Ping>
    {
        public uint Seq;
        public uint SendTimeMs;
        // 최신 RTT
        public double RttMs;
        // EWMA 평균 RTT
        public double AvgRttMs;
        // EWMA 지터
        public double JitterMs;
    }

    [ProtocolData(ProtocolId.C2S.CloseCmd)]
    internal class CloseCmd : ProtocolDataBase<CloseCmd>
    {
        
    }

    [ProtocolData(ProtocolId.C2S.UdpHelloReq)]
    internal class UdpHelloReq : ProtocolDataBase<UdpHelloReq>
    {
        public ushort Mtu;
        public uint PeerId;
        public long SessionId;
    }

    [ProtocolData(ProtocolId.C2S.UdpHealthCheckAck)]
    internal class UdpHealthCheckAck : ProtocolDataBase<UdpHealthCheckAck>
    {
        
    }
}
