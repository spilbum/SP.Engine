using SP.Engine.Runtime;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Common.Protocol.C2S
{
    [ProtocolData(ProtocolId.C2S.SessionAuthReq)]
    internal partial class SessionAuthReq
    {
        public byte[]? EncryptPublicKey;
        public DhKeySize EncryptKeySize;
        public PeerKind PeerKind;
        public uint NextExpectedSeq;
        public uint PeerId;
        public long SessionId;
    }

    [ProtocolData(ProtocolId.C2S.MessageAck)]
    internal partial class MessageAck
    {
        public uint NextExpectedSeq;  
    }

    [ProtocolData(ProtocolId.C2S.Ping)]
    internal partial class Ping
    {
        public uint SendTimeMs;
        // 최신 RTT
        public double RttMs;
        // EWMA 평균 RTT
        public double AvgRttMs;
        // EWMA 지터
        public double JitterMs;
    }

    [ProtocolData(ProtocolId.C2S.CloseCmd)]
    internal partial class CloseCmd
    {
        
    }

    [ProtocolData(ProtocolId.C2S.UdpHelloReq)]
    internal partial class UdpHelloReq
    {
        public ushort Mtu;
        public uint PeerId;
        public long SessionId;
    }

    [ProtocolData(ProtocolId.C2S.UdpHealthCheckAck)]
    internal partial class UdpHealthCheckAck
    {
        
    }
}
