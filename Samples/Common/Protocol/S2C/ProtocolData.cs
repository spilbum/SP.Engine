using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Protocol;

namespace Common.Protocol.S2C;

[ProtocolData(ProtocolId.S2C.TcpEchoAck)]
public class TcpEchoAck : ProtocolDataBase<TcpEchoAck>
{
    public long SentTicks;
    public byte[]? Data;
}

[ProtocolData(ProtocolId.S2C.UdpEchoAck, ChannelKind.Unreliable, Toggle.Off, Toggle.Off)]
public class UdpEchoAck : ProtocolDataBase<UdpEchoAck>
{
    public long SentTicks;
    public byte[]? Data;
}
