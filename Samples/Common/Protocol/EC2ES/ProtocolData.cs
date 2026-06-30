using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;

namespace Common.Protocol.EC2ES;

[ProtocolData(ProtocolId.EC2ES.TcpEchoReq)]
public class TcpEchoReq : ProtocolDataBase<TcpEchoReq>
{
    public long SentTicks;
    public byte[]? Data;
}

[ProtocolData(ProtocolId.EC2ES.UdpEchoReq, ChannelKind.Unreliable, Toggle.Off, Toggle.Off)]
public class UdpEchoReq : ProtocolDataBase<UdpEchoReq>
{
    public long SentTicks;
    public byte[]? Data;
}

[ProtocolData(ProtocolId.EC2ES.HeavyLoadNotify)]
public class HeavyLoadNotify : ProtocolDataBase<HeavyLoadNotify>
{
    public int DelayMs;
}

