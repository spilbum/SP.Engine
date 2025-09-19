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
            public const EProtocolId UdpHelloReq = (EProtocolId)104;
            public const EProtocolId UdpKeepAlive = (EProtocolId)105;
        }

        public static class S2C
        {
            public const EProtocolId SessionAuthAck = (EProtocolId)200;
            public const EProtocolId MessageAck = (EProtocolId)201;
            public const EProtocolId Pong = (EProtocolId)202;
            public const EProtocolId Close = (EProtocolId)203;
            public const EProtocolId UdpHelloAck = (EProtocolId)204;
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
                    public EDhKeySize KeySize;
                    public byte[]? ClientPublicKey;
                    public ushort UdpMtu;
                }
            
                [ProtocolData(EngineProtocol.C2S.MessageAck)]
                public class MessageAck : BaseProtocolData
                {
                    public long SequenceNumber;
                }
            
                [ProtocolData(EngineProtocol.C2S.Ping)]
                public class Ping : BaseProtocolData
                {
                    public double RawRttMs;
                    public double AvgRttMs;
                    public double JitterMs;
                    public float PacketLossRate;
                    public long SendTimeMs;
                }
            
                [ProtocolData(EngineProtocol.C2S.Close)]
                public class Close : BaseProtocolData
                {
                }

                [ProtocolData(EngineProtocol.C2S.UdpHelloReq, EProtocolType.Udp)]
                public class UdpHelloReq : BaseProtocolData
                {
                    public string? SessionId;
                    public EPeerId PeerId;
                }

                [ProtocolData(EngineProtocol.C2S.UdpKeepAlive, EProtocolType.Udp)]
                public class UdpKeepAlive : BaseProtocolData
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

                public int MaxFrameBytes;
                public int SendTimeoutMs;
                public int MaxResendCount;

                public int UdpOpenPort;
            }

            [ProtocolData(EngineProtocol.S2C.Pong)]
            public class Pong : BaseProtocolData
            {
                public long SendTimeMs;
                public long ServerTimeMs;
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

            [ProtocolData(EngineProtocol.S2C.UdpHelloAck, EProtocolType.Udp)]
            public class UdpHelloAck : BaseProtocolData
            {
                public EEngineErrorCode ErrorCode;
            }
        }
    }
    
    
}
