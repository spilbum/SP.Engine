using System;
using SP.Engine.Core.Utility;
using SP.Engine.Core.Utility.Crypto;

namespace SP.Engine.Core.Protocol
{
    public static class EngineProtocolDataC2S
    {
        [Protocol(EngineProtocolIdC2S.AuthReq)]
        public class AuthReq : BaseProtocolData
        {
            public string SessionId { get; set; }
            public EPeerId PeerId { get; set; }
            public DhKeySize KeySize{ get; set; }
            public byte[] ClientPublicKey { get; set; }
        }

        [Protocol(EngineProtocolIdC2S.NotifyMessageAckInfo)]
        public class NotifyMessageAckInfo : BaseProtocolData
        {
            public long SequenceNumber { get; set; }
        }

        [Protocol(EngineProtocolIdC2S.NotifyPingInfo)]
        public class NotifyPingInfo : BaseProtocolData
        {
            public int LatencyAverageMs { get; set; }
            public int LatencyStandardDeviationMs { get; set; }
            public DateTime SendTime { get; set; }
        }

        [Protocol(EngineProtocolIdC2S.NotifyClose)]
        public class NotifyClose : BaseProtocolData
        {
        }
    }

    public static class EngineProtocolDataS2C
    {
        [Protocol(EngineProtocolIdS2C.NotifyPongInfo)]
        public class NotifyPongInfo : BaseProtocolData
        {
            public DateTime ServerTime { get; set; }
            public DateTime SentTime { get; set; }
        }

        [Protocol(EngineProtocolIdS2C.AuthAck)]
        public class AuthAck : BaseProtocolData
        {
            public ESystemErrorCode ErrorCode { get; set; }
            public string SessionId { get; set; }
            public EPeerId PeerId { get; set; }
            public byte[] ServerPublicKey { get; set; }
            public byte[] Signature { get; set; }
            public int LimitRequestLength { get; set; }
            public int SendTimeOutMs { get; set; }
            public int MaxReSendCnt { get; set; }
        }

        [Protocol(EngineProtocolIdS2C.NotifyMessageAckInfo)]
        public class NotifyMessageAckInfo : BaseProtocolData
        {
            public long SequenceNumber { get; set; }
        }

        [Protocol(EngineProtocolIdS2C.NotifyClose)]
        public class NotifyClose : BaseProtocolData
        {
        }
    }
}
