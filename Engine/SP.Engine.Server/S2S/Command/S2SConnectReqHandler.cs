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
        if (result != S2SConnectResult.Success)
        {
            context.Logger.Warn("[S2S] Failed to connect to the S2S server. Result: {0}", result);
        }

        using var scope = ProtocolScope<S2SConnectAck>.Rent();
        scope.Protocol.Result = result;
        context.InternalSend(scope.Protocol);
    }
}
