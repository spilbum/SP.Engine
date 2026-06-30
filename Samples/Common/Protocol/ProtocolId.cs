namespace Common.Protocol;

public static class ProtocolId
{
    public static class EC2ES
    {
        public const ushort TcpEchoReq = 1000;
        public const ushort UdpEchoReq = 1001;
        public const ushort HeavyLoadNotify = 1002;
    }
    
    public static class ES2EC
    {
        public const ushort TcpEchoAck = 2000;
        public const ushort UdpEchoAck = 2001;
    }

    public static class CC2CS
    {
        public const ushort LoginReq = 1000;
        public const ushort ChatNotifyReq = 1001;
    }

    public static class CS2CC
    {
        public const ushort LoginAck = 2000;
        public const ushort ChatNotify = 2001;
    }

    public static class CS2CS
    {
        public const ushort ChatNotify = 3000;
    }
}
