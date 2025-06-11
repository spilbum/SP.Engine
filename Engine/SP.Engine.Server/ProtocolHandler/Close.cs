using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(EngineProtocol.C2S.Close)]
internal class Close<TPeer> : BaseEngineHandler<ClientSession<TPeer>, EngineProtocolData.C2S.Close>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(ClientSession<TPeer> session, EngineProtocolData.C2S.Close protocol)
    {
        session.Logger.Debug("Received a termination request from the client. isClosing={0}", session.IsClosing);
        if (session.IsClosing)
        {
            session.Close(ECloseReason.ClientClosing);
            return;
        }

        session.SendCloseHandshake();
        session.Close(ECloseReason.ClientClosing);
    }
}
