using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Protocol;

namespace EchoServer.Protocol;

public static class C2S
{
    public const ushort TcpEchoReq = 1000;
    public const ushort UdpEchoReq = 1001;
}

[ProtocolData(C2S.TcpEchoReq)]
public class C2S_TcpEchoReq : ProtocolDataBase<C2S_TcpEchoReq>
{
    public long SentTicks;
    public byte[]? Data;
}

[ProtocolData(C2S.UdpEchoReq, ChannelKind.Unreliable, Toggle.Off, Toggle.Off)]
public class C2S_UdpEchoReq : ProtocolDataBase<C2S_UdpEchoReq>
{
    public long SentTicks;
    public byte[]? Data;
}
