using Common;
using RankServer.ServerPeer;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace RankServer.Command;

[ProtocolCommand(S2SProtocol.RegisterReq)]
public class RegisterReq : BaseCommand<BaseServerPeer, S2SProtocolData.RegisterReq>
{
    protected override void ExecuteProtocol(BaseServerPeer context, S2SProtocolData.RegisterReq protocol)
    {
        var ack = new S2SProtocolData.RegisterAck { Result = ErrorCode.Unknown };

        if (string.IsNullOrEmpty(protocol.ServerKind))
        {
            ack.Result = ErrorCode.InvalidRequest;
            context.Send(ack);
            return;
        }
        
        var errorCode = RankServer.Instance.RegisterPeer(protocol, context);
        if (errorCode != ErrorCode.Ok)
        {
            ack.Result = errorCode;
            context.Send(ack);
            return;
        }

        ack.Result = ErrorCode.Ok;
        context.Send(ack);
    }
}
