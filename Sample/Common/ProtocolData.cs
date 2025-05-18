using SP.Engine.Runtime.Protocol;

namespace Common
{
    public static class ProtocolData
    {
        public static class C2S
        {
            [ProtocolData(Protocol.C2S.Echo)]
            public class Echo : BaseProtocolData
            {
                public string? Text { get; set; }
            }
        }

        public static class S2C
        {
            [ProtocolData(Protocol.S2C.Echo)]
            public class Echo : BaseProtocolData
            {
                public string? Text { get; set; }
            }
        }
    }
}


