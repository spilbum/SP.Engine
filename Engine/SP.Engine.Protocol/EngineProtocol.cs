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
                    public string? SessionId;
                    public EPeerId PeerId;
                    public DhKeySize KeySize;
                    public byte[]? ClientPublicKey;
                }
            
                [ProtocolData(EngineProtocol.C2S.MessageAck)]
                public class MessageAck : BaseProtocolData
                {
                    public long SequenceNumber;
                }
            
                [ProtocolData(EngineProtocol.C2S.Ping)]
                public class Ping : BaseProtocolData
                {
                    public int LatencyAverageMs;
                    public int LatencyStandardDeviationMs;
                    public DateTime SendTime;
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
                public EEngineErrorCode ErrorCode;
                public string? SessionId;
                public EPeerId PeerId;

                public bool UseEncryption;
                public byte[]? ServerPublicKey;
                
                public bool UseCompression;
                public int CompressionThresholdPercent;
                
                public int MaxAllowedLength;
                public int SendTimeOutMs;
                public int MaxReSendCnt;
                
            }

            [ProtocolData(EngineProtocol.S2C.Pong)]
            public class Pong : BaseProtocolData
            {
                public DateTime ServerTime;
                public DateTime SentTime;
            }

            [ProtocolData(EngineProtocol.S2C.MessageAck)]
            public class MessageAck : BaseProtocolData
            {
                public long SequenceNumber;
            }

            [ProtocolData(EngineProtocol.S2C.Close)]
            public class Close : BaseProtocolData
            {
            }
        }
    }
    
    
}
