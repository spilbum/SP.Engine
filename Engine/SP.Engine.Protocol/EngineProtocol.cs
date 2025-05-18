using System;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Protocol
{
    public static class EngineProtocol
    {
        public static class C2S
        {
            public const EProtocolId SessionAuthReq = (EProtocolId)100;
            public const EProtocolId MessageAck = (EProtocolId)101;
            public const EProtocolId Ping = (EProtocolId)102;
            public const EProtocolId Close = (EProtocolId)103;
        }

        public static class S2C
        {
            public const EProtocolId SessionAuthAck = (EProtocolId)200;
            public const EProtocolId MessageAck = (EProtocolId)201;
            public const EProtocolId Pong = (EProtocolId)202;
            public const EProtocolId Close = (EProtocolId)203;
        }
    }

    public static class EngineProtocolData
    {
        public static class C2S
        {
                [ProtocolData(EngineProtocol.C2S.SessionAuthReq)]
                public class SessionAuthReq : BaseProtocolData
                {
                    public string? SessionId { get; set; }
                    public EPeerId PeerId { get; set; }
                    public DhKeySize KeySize { get; set; }
                    public byte[]? ClientPublicKey { get; set; }
                }
            
                [ProtocolData(EngineProtocol.C2S.MessageAck)]
                public class MessageAck : BaseProtocolData
                {
                    public long SequenceNumber { get; set; }
                }
            
                [ProtocolData(EngineProtocol.C2S.Ping)]
                public class Ping : BaseProtocolData
                {
                    public int LatencyAverageMs { get; set; }
                    public int LatencyStandardDeviationMs { get; set; }
                    public DateTime SendTime { get; set; }
                }
            
                [ProtocolData(EngineProtocol.C2S.Close)]
                public class Close : BaseProtocolData
                {
                }
        }

        public static class S2C
        {
            [ProtocolData(EngineProtocol.S2C.SessionAuthAck)]
            public class SessionAuthAck : BaseProtocolData
            {
                public EEngineErrorCode ErrorCode { get; set; }
                public string? SessionId { get; set; }
                public EPeerId PeerId { get; set; }
                public byte[]? ServerPublicKey { get; set; }
                public int MaxAllowedLength { get; set; }
                public int SendTimeOutMs { get; set; }
                public int MaxReSendCnt { get; set; }
            }

            [ProtocolData(EngineProtocol.S2C.Pong)]
            public class Pong : BaseProtocolData
            {
                public DateTime ServerTime { get; set; }
                public DateTime SentTime { get; set; }
            }

            [ProtocolData(EngineProtocol.S2C.MessageAck)]
            public class MessageAck : BaseProtocolData
            {
                public long SequenceNumber { get; set; }
            }

            [ProtocolData(EngineProtocol.S2C.Close)]
            public class Close : BaseProtocolData
            {
            }
        }
    }
    
    
}
