namespace SP.Engine.Common.Protocol
{
    internal static class ProtocolId
    {
        public static class C2S
        {
            public const ushort SessionAuthReq = 100;
            public const ushort MessageAck = 101;
            public const ushort Ping = 102;
            public const ushort CloseCmd = 103;
            public const ushort UdpHelloReq = 104;
            public const ushort UdpHealthCheckAck = 105;
        }

        public static class S2C
        {
            public const ushort SessionAuthAck = 200;
            public const ushort MessageAck = 201;
            public const ushort Pong = 202;
            public const ushort CloseCmd = 203;
            public const ushort UdpHelloAck = 204;
            public const ushort UdpHealthCheckReq = 205;
            public const ushort UdpStatusNotify = 206;
        }

        public static class S2S
        {
            public const ushort S2SConnectReq = 300;
            public const ushort S2SConnectAck = 301;
        }
    }

}
