using System;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Protocol;

namespace SP.Protocol
{
    public static class S2CEngineProtocol
    {
        public const EProtocolId SessionAuthAck = (EProtocolId)200;
        public const EProtocolId MessageAck = (EProtocolId)201;
        public const EProtocolId Pong = (EProtocolId)202;
        public const EProtocolId Close = (EProtocolId)203;

        public static class Data
        {
            [ProtocolData(S2CEngineProtocol.SessionAuthAck)]
            public class SessionAuthAck : BaseProtocolData
            {
                public EEngineErrorCode ErrorCode { get; set; }
                public string? SessionId { get; set; }
                public EPeerId PeerId { get; set; }
                public byte[]? ServerPublicKey { get; set; }
                public int LimitRequestLength { get; set; }
                public int SendTimeOutMs { get; set; }
                public int MaxReSendCnt { get; set; }
            }

            [ProtocolData(S2CEngineProtocol.Pong)]
            public class Pong : BaseProtocolData
            {
                public DateTime ServerTime { get; set; }
                public DateTime SentTime { get; set; }
            }

            [ProtocolData(S2CEngineProtocol.MessageAck)]
            public class MessageAck : BaseProtocolData
            {
                public long SequenceNumber { get; set; }
            }

            [ProtocolData(S2CEngineProtocol.Close)]
            public class Close : BaseProtocolData
            {
            }
        }
    }
}
