using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(C2SEngineProtocolId.Close)]
internal class Close<TPeer> : BaseEngineHandler<ClientSession<TPeer>, C2SEngineProtocolData.Close>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(ClientSession<TPeer> session, C2SEngineProtocolData.Close data)
    {
        session.Logger.Debug("Received a termination request from the client. isClosing={0}", session.IsClosing);
        if (session.IsClosing)
        {
            session.Close(CloseReason.ClientClosing);
            return;
        }

        session.SendCloseHandshake();
        session.Close(CloseReason.ClientClosing);
    }
}
