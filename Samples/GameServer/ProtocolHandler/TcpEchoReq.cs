using Common;
using SP.Engine.Runtime.Handler;
using SP.Engine.Server.ProtocolHandler;

namespace GameServer.ProtocolHandler;

[ProtocolHandler(C2SProtocol.TcpEchoReq)]
public class TcpEchoReq : BaseProtocolHandler<GamePeer, C2SProtocolData.TcpEchoReq>
{
    protected override void ExecuteProtocol(GamePeer peer, C2SProtocolData.TcpEchoReq data)
    {
        peer.Send(new S2CProtocolData.TcpEchoAck { SentTime = data.SendTime, Data = data.Data });
    }
}
