using System;
using SP.Engine.Runtime.Protocol;

namespace Common
{
    public static class C2SProtocol
    {
        public const EProtocolId UdpEchoReq = (EProtocolId)10000;
    }

    public static class S2CProtocol
    {
        public const EProtocolId UdpEchoAck = (EProtocolId)20000;
    }

    public static class C2SProtocolData
    {
        [ProtocolData(C2SProtocol.UdpEchoReq, EProtocolType.Udp)]
        public class UdpEchoReq : BaseProtocolData
        {
            public float SendTime;
            public byte[] Data;
        }
    }

    public static class S2CProtocolData
    {
        [ProtocolData(S2CProtocol.UdpEchoAck, EProtocolType.Udp)]
        public class UdpEchoAck : BaseProtocolData
        {
            public float SentTime;
            public byte[] Data;
        }
    }
}


