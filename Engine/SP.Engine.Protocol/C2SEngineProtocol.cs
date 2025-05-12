using System;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Protocol
{
    public static class C2SEngineProtocol
    {
        public const EProtocolId SessionAuthReq = (EProtocolId)100;
        public const EProtocolId MessageAck = (EProtocolId)101;
        public const EProtocolId Ping = (EProtocolId)102;
        public const EProtocolId Close = (EProtocolId)103;
    }

    public static class C2SEngineProtocolData
    {
        [ProtocolData(C2SEngineProtocol.SessionAuthReq)]
        public class SessionAuthReq : BaseProtocolData
        {
            public string? SessionId { get; set; }
            public EPeerId PeerId { get; set; }
            public DhKeySize KeySize { get; set; }
            public byte[]? ClientPublicKey { get; set; }
        }

        [ProtocolData(C2SEngineProtocol.MessageAck)]
        public class MessageAck : BaseProtocolData
        {
            public long SequenceNumber { get; set; }
        }

        [ProtocolData(C2SEngineProtocol.Ping)]
        public class Ping : BaseProtocolData
        {
            public int LatencyAverageMs { get; set; }
            public int LatencyStandardDeviationMs { get; set; }
            public DateTime SendTime { get; set; }
        }

        [ProtocolData(C2SEngineProtocol.Close)]
        public class Close : BaseProtocolData
        {
        }
    }
}
