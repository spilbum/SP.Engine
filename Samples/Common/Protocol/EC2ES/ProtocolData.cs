using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Protocol;

namespace Common.Protocol.EC2ES;

[ProtocolData(ProtocolId.EC2ES.TcpEchoReq)]
public partial class TcpEchoReq
{
    public long SentTicks;
    public byte[]? Data;
}

[ProtocolData(ProtocolId.EC2ES.UdpEchoReq, Channel = ChannelKind.Unreliable, Encrypt = Toggle.Off)]
public partial class UdpEchoReq
{
    public long SentTicks;
    public byte[]? Data;
}

[ProtocolData(ProtocolId.EC2ES.HeavyLoadNotify)]
public partial class HeavyLoadNotify
{
    public int DelayMs;
}

