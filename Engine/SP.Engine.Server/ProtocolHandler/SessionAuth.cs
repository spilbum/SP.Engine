using SP.Engine.Protocol;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(C2SEngineProtocolId.SessionAuthReq)]
internal class SessionAuth<TPeer> : BaseEngineHandler<Session<TPeer>, C2SEngineProtocolData.SessionAuthReq>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(Session<TPeer> session, C2SEngineProtocolData.SessionAuthReq data)
    {   
        session.OnSessionHandshake(data);
    }
}
