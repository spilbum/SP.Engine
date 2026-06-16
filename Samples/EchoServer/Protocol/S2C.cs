using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Protocol;

namespace EchoServer.Protocol;

public static class S2C
{
    public const ushort TcpEchoAck = 2000;
    public const ushort UdpEchoAck = 2001;
}

[ProtocolData(S2C.TcpEchoAck)]
public class S2C_TcpEchoAck : ProtocolDataBase<S2C_TcpEchoAck>
{
    public long SentTicks;
    public byte[]? Data;
}

[ProtocolData(S2C.UdpEchoAck, ChannelKind.Unreliable, Toggle.Off, Toggle.Off)]
public class S2C_UdpEchoAck : ProtocolDataBase<S2C_UdpEchoAck>
{
    public long SentTicks;
    public byte[]? Data;
}
