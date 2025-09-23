using Common;
using SP.Engine.Runtime.Handler;
using SP.Engine.Server.ProtocolHandler;

namespace GameServer.ProtocolHandler;

[ProtocolHandler(C2SProtocol.UdpEchoReq)]
public class UdpEchoReq : BaseProtocolHandler<GamePeer, C2SProtocolData.UdpEchoReq>
{
    protected override void ExecuteProtocol(GamePeer peer, C2SProtocolData.UdpEchoReq data)
    {
        peer.Send(new S2CProtocolData.UdpEchoAck { SentTime = data.SendTime, Data = data.Data });
    }
}
