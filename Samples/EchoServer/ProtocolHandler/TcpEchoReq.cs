using Common;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.ProtocolHandler;

namespace EchoServer.ProtocolHandler;

[ProtocolCommand(C2SProtocol.TcpEchoReq)]
public class TcpEchoReq : BaseCommand<EchoPeer, C2SProtocolData.TcpEchoReq>
{
    protected override void ExecuteProtocol(EchoPeer peer, C2SProtocolData.TcpEchoReq data)
    {
        peer.Logger.Debug("TCP echo request received");
        peer.Send(new S2CProtocolData.TcpEchoAck { SentTime = data.SendTime, Data = data.Data });
    }
}
