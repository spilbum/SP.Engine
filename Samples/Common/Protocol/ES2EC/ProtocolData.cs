using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Protocol;

namespace Common.Protocol.ES2EC;

[ProtocolData(ProtocolId.ES2EC.TcpEchoAck)]
public partial class TcpEchoAck
{
    public long SentTicks;
    public byte[]? Data;
}

[ProtocolData(ProtocolId.ES2EC.UdpEchoAck, Channel = ChannelKind.Unreliable, Encrypt = Toggle.Off)]
public partial class UdpEchoAck
{
    public long SentTicks;
    public byte[]? Data;
}
