namespace Common.Protocol;

public static class ProtocolId
{
    public static class C2S
    {
        public const ushort TcpEchoReq = 1000;
        public const ushort UdpEchoReq = 1001;
    }
    
    public static class S2C
    {
        public const ushort TcpEchoAck = 2000;
        public const ushort UdpEchoAck = 2001;
    }
}
