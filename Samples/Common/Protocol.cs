using System;
using SP.Engine.Runtime.Protocol;

namespace Common
{
    public static class C2SProtocol
    {
        public const EProtocolId TcpEchoReq = (EProtocolId)10000;
        public const EProtocolId UdpEchoReq = (EProtocolId)10001;
    }

    public static class S2CProtocol
    {
        public const EProtocolId TcpEchoAck = (EProtocolId)20000;
        public const EProtocolId UdpEchoAck = (EProtocolId)20001;
    }

    public static class C2SProtocolData
    {
        [ProtocolData(C2SProtocol.TcpEchoReq)]
        public class TcpEchoReq : BaseProtocolData
        {
            public float SendTime;
            public byte[]? Data;
        }
        
        [ProtocolData(C2SProtocol.UdpEchoReq, EProtocolType.Udp)]
        public class UdpEchoReq : BaseProtocolData
        {
            public float SendTime;
            public byte[]? Data;
        }
    }

    public static class S2CProtocolData
    {
        [ProtocolData(S2CProtocol.TcpEchoAck)]
        public class TcpEchoAck : BaseProtocolData
        {
            public float SentTime;
            public byte[]? Data;
        }
        
        [ProtocolData(S2CProtocol.UdpEchoAck, EProtocolType.Udp)]
        public class UdpEchoAck : BaseProtocolData
        {
            public float SentTime;
            public byte[]? Data;
        }
    }
}


