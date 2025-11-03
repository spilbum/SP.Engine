using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;
using SP.Sample.RankServer.ServerPeer;

namespace SP.Sample.RankServer.Command;

[ProtocolCommand(S2SProtocol.RegisterReq)]
public class RegisterReq : BaseCommand<BaseServerPeer, S2SProtocolData.RegisterReq>
{
    protected override void ExecuteProtocol(BaseServerPeer context, S2SProtocolData.RegisterReq protocol)
    {
        var ack = new S2SProtocolData.RegisterAck { Result = ErrorCode.Unknown };

        var peer = protocol.Name switch
        {
            "Game" => new GameServerPeer(context),
            _ => throw new InvalidCastException($"Unknown server name: {protocol.Name}")
        };

        if (!RankServer.Instance.RegisterPeer(peer))
        {
            ack.Result = ErrorCode.InternalError;
            context.Send(ack);
            return;
        }

        context.Logger.Info("Server {0} registered", protocol.Name);
        ack.Result = ErrorCode.Ok;
        context.Send(ack);
    }
}
