using Common;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.ProtocolHandler;

namespace EchoServer.ProtocolHandler;

[ProtocolCommand(C2SProtocol.UdpEchoReq)]
public class UdpEchoReq : BaseCommand<EchoPeer, C2SProtocolData.UdpEchoReq>
{
    protected override void ExecuteProtocol(EchoPeer peer, C2SProtocolData.UdpEchoReq data)
    {
        peer.Logger.Debug("UDP echo request received");
        peer.Send(new S2CProtocolData.UdpEchoAck { SentTime = data.SendTime, Data = data.Data });
    }
}
