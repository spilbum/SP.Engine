using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;

namespace Common.Protocol.ES2EC;

[ProtocolData(ProtocolId.ES2EC.TcpEchoAck)]
public class TcpEchoAck : ProtocolDataBase<TcpEchoAck>
{
    public long SentTicks;
    public byte[]? Data;
}

[ProtocolData(ProtocolId.ES2EC.UdpEchoAck, ChannelKind.Unreliable, Toggle.Off, Toggle.Off)]
public class UdpEchoAck : ProtocolDataBase<UdpEchoAck>
{
    public long SentTicks;
    public byte[]? Data;
}
