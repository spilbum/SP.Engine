using NetworkCommon;
using SP.Engine.Runtime.Handler;
using SP.Engine.Server.Handler;

namespace SP.GameServer.ProtocolHandler;

[ProtocolHandler(C2SProtocol.LoginReq)]
public class LoginReq : BaseProtocolHandler<UserPeer, C2SProtocol.Data.LoginReq>
{
    protected override void ExecuteProtocol(UserPeer peer, C2SProtocol.Data.LoginReq protocol)
    {
        peer.Send(new S2CProtocol.Data.LoginAck { Uid = protocol.Uid });
    }
}
