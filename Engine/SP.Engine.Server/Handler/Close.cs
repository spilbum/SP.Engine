using SP.Engine.Runtime;
using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Server.Handler;

[ProtocolHandler(C2SEngineProtocol.Close)]
internal class Close<TPeer> : BaseEngineHandler<Session<TPeer>, C2SEngineProtocolData.Close>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(Session<TPeer> context, C2SEngineProtocolData.Close protocol)
    {
        context.Logger.Debug("Received a termination request from the client. isClosing={0}", context.IsClosing);
        if (context.IsClosing)
        {
            context.Close(ECloseReason.ClientClosing);
            return;
        }

        context.SendCloseHandshake();
        context.Close(ECloseReason.ClientClosing);
    }
}
