using System;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Protocol;

namespace Common
{
    public static class C2SProtocol
    {
        public const ushort TcpEchoReq = 10000;
        public const ushort UdpEchoReq = 10001;
    }

    public static class S2CProtocol
    {
        public const ushort TcpEchoAck = 20000;
        public const ushort UdpEchoAck = 20001;
    }

    public static class C2SProtocolData
    {
        [Protocol(C2SProtocol.TcpEchoReq)]
        public class TcpEchoReq : BaseProtocol
        {
            public float SendTime;
            public byte[]? Data;
        }
        
        [Protocol(C2SProtocol.UdpEchoReq, ChannelKind.Unreliable)]
        public class UdpEchoReq : BaseProtocol
        {
            public float SendTime;
            public byte[]? Data;
        }
    }

    public static class S2CProtocolData
    {
        [Protocol(S2CProtocol.TcpEchoAck)]
        public class TcpEchoAck : BaseProtocol
        {
            public float SentTime;
            public byte[]? Data;
        }
        
        [Protocol(S2CProtocol.UdpEchoAck, ChannelKind.Unreliable)]
        public class UdpEchoAck : BaseProtocol
        {
            public float SentTime;
            public byte[]? Data;
        }
    }
}


