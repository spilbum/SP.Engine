using SP.Engine.Runtime.Handler;
using SP.Engine.Runtime.Protocol;

namespace NetworkCommon
{
    public static class Protocol
    {
        public static class C2ES
        {
            public const EProtocolId EchoReq = (EProtocolId)10000;
        }

        public static class ES2C
        {
            public const EProtocolId EchoAck = (EProtocolId)20000;
        }

        public static class S2SS
        {
            public const EProtocolId RegisterServerReq = (EProtocolId)40000;
        }
        
        public static class SS2S
        {
            public const EProtocolId RegisterServerAck = (EProtocolId)30000;
        }
    }

    public static class ProtocolData
    {
        public static class C2S
        {
            [ProtocolData(Protocol.C2ES.EchoReq, isEncrypt: true)]
            public class EchoReq : BaseProtocolData
            {
                public string? Message { get; set; }
            }
        }

        public static class S2C
        {
            [ProtocolData(Protocol.ES2C.EchoAck, isEncrypt: true)]
            public class EchoAck : BaseProtocolData
            {
                public string? Message { get; set; }
            }
        }

        public static class S2SS
        {
            [ProtocolData(Protocol.S2SS.RegisterServerReq)]
            public class RegisterServerReq : BaseProtocolData
            {
                public string? ServerType { get; set; }
            }
        }
        
        public static class SS2S
        {
            [ProtocolData(Protocol.SS2S.RegisterServerAck)]
            public class RegisterServerAck : BaseProtocolData
            {
                public int ErrorCode { get; set; }
            }
        }
    }
}

