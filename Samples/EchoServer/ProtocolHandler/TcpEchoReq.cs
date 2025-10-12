using Common;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.ProtocolHandler;

namespace EchoServer.ProtocolHandler;

[ProtocolHandler(C2SProtocol.TcpEchoReq)]
public class TcpEchoReq : BaseProtocolHandler<EchoPeer, C2SProtocolData.TcpEchoReq>
{
    protected override void ExecuteProtocol(EchoPeer peer, C2SProtocolData.TcpEchoReq data)
    {
        peer.Send(new S2CProtocolData.TcpEchoAck { SentTime = data.SendTime, Data = data.Data });
    }
}
