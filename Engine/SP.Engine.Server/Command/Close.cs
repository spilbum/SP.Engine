using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.Close)]
internal class Close : BaseCommand<Session, C2SEngineProtocolData.Close>
{
    protected override void ExecuteProtocol(Session session, C2SEngineProtocolData.Close protocol)
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
