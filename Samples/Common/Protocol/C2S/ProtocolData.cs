using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Protocol;

namespace Common.Protocol.C2S;

[ProtocolData(ProtocolId.C2S.TcpEchoReq)]
public class TcpEchoReq : ProtocolDataBase<TcpEchoReq>
{
    public long SentTicks;
    public byte[]? Data;
}

[ProtocolData(ProtocolId.C2S.UdpEchoReq, ChannelKind.Unreliable, Toggle.Off, Toggle.Off)]
public class UdpEchoReq : ProtocolDataBase<UdpEchoReq>
{
    public long SentTicks;
    public byte[]? Data;
}

