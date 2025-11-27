using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.LoginAck)]
public class LoginAck : BaseCommand<NetworkClient, G2CProtocolData.LoginAck>
{
    protected override void ExecuteProtocol(NetworkClient context, G2CProtocolData.LoginAck protocol)
    {
        if (protocol.Result != ErrorCode.Ok)
        {
            context.Logger.Error("LoginAck failed: {0}", protocol.Result);
            return;
        }

        context.OnLogin(protocol.Uid, protocol.AccessToken);
        context.Logger.Debug("User logged in: {0}, accessToken={1}", protocol.Uid, protocol.AccessToken);
    }
}
