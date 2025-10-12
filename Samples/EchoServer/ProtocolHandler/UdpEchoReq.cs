using Common;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.ProtocolHandler;

namespace EchoServer.ProtocolHandler;

[ProtocolHandler(C2SProtocol.UdpEchoReq)]
public class UdpEchoReq : BaseProtocolHandler<EchoPeer, C2SProtocolData.UdpEchoReq>
{
    protected override void ExecuteProtocol(EchoPeer peer, C2SProtocolData.UdpEchoReq data)
    {
        peer.Send(new S2CProtocolData.UdpEchoAck { SentTime = data.SendTime, Data = data.Data });
    }
}
