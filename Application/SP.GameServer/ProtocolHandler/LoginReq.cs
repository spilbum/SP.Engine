using NetworkCommon;
using SP.Engine.Runtime.Handler;
using SP.Engine.Server.Handler;

namespace SP.GameServer.ProtocolHandler;

[ProtocolHandler(C2SProtocol.LoginReq)]
public class LoginReq : BaseProtocolHandler<UserPeer, C2SProtocolData.LoginReq>
{
    protected override void ExecuteProtocol(UserPeer peer, C2SProtocolData.LoginReq protocol)
    {
        peer.Send(new S2CProtocolData.LoginAck { Uid = protocol.Uid });
    }
}
