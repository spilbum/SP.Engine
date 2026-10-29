using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.S2S;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Protocol;

[ProtocolHandler(ProtocolId.S2S.S2SConnectReq)]
internal sealed class S2SConnectReqHandler : ProtocolHandlerBase<Session, S2SConnectReq>
{
    protected override void ExecuteProtocol(Session context, S2SConnectReq protocol)
    {
        var result = context.Engine.ConnectS2SPeer(context, protocol.Category);

        using var scope = ProtocolScope<S2SConnectAck>.Rent();
        scope.Protocol.Result = result;
        context.InternalSend(scope.Protocol);
    }
}
