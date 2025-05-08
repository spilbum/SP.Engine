using NetworkCommon;
using SP.Engine.Runtime.Handler;
using SP.Engine.Server.Handler;

namespace SP.GameServer.ProtocolHandler;

[ProtocolHandler(C2SProtocol.LoginReq)]
public class LoginReq : BaseProtocolHandler<UserPeer, C2SProtocol.Data.LoginReq>
{
    protected override void ExecuteProtocol(UserPeer peer, C2SProtocol.Data.LoginReq protocol)
    {
        peer.Logger.Debug("LoginReq received. uid={0}, sendTime={1}", protocol.Uid, protocol.SendTime);
        peer.Send(new S2CProtocol.Data.LoginAck { Uid = protocol.Uid, SentTime = protocol.SendTime });
    }
}
