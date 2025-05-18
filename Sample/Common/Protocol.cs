using SP.Engine.Runtime.Protocol;

namespace Common
{
    public static class Protocol
    {
        public static class C2S
        {
            public const EProtocolId Echo = (EProtocolId)1000;
        }

        public static class S2C
        {
            public const EProtocolId Echo = (EProtocolId)2000;
        }
    }
}


