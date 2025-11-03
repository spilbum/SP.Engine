using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.SessionAuthReq)]
internal class SessionAuth : BaseCommand<Session, C2SEngineProtocolData.SessionAuthReq>
{
    protected override void ExecuteProtocol(Session session, C2SEngineProtocolData.SessionAuthReq protocol)
    {
        session.OnSessionHandshake(protocol);
    }
}
