using System;
using SP.Engine.Runtime.Protocol;

namespace Common
{
    public static class Protocol
    {
        public static class C2S
        {
            public const EProtocolId EchoReq = (EProtocolId)1000;
        }

        public static class S2C
        {
            public const EProtocolId EchoAck = (EProtocolId)2000;
        }
    }

    public static class ProtocolData
    {
        public static class C2S
        {
            [ProtocolData(Protocol.C2S.EchoReq)]
            public class EchoReq : BaseProtocolData
            {
                public string? Str;
                public byte[]? Bytes;
                public DateTime SendTime;
            }
        }

        public static class S2C
        {
            [ProtocolData(Protocol.S2C.EchoAck)]
            public class EchoAck : BaseProtocolData
            {
                public string? Str;
                public byte[]? Bytes;
                public DateTime SentTime;
            }
        }
    }
}


