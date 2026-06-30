using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.S2S;
using SP.Engine.Runtime.Command;
using SP.Engine.Server.Protocol;

namespace SP.Engine.Server.S2S.Command;

[CommandHandler(ProtocolId.S2S.S2SConnectReq)]
internal sealed class S2SConnectReqHandler : CommandHandlerBase<Session, S2SConnectReq>
{
    protected override void ExecuteCommand(Session context, S2SConnectReq protocol)
    {
        var result = context.Engine.ConnectS2SPeer(context, protocol.Category);

        using var scope = ProtocolScope<S2SConnectAck>.Rent();
        scope.Protocol.Result = result;
        context.InternalSend(scope.Protocol);
    }
}
