using SP.Engine.Protocol;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(C2SEngineProtocolId.SessionAuthReq)]
internal class SessionAuth<TPeer> : BaseEngineHandler<ClientSession<TPeer>, C2SEngineProtocolData.SessionAuthReq>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(ClientSession<TPeer> session, C2SEngineProtocolData.SessionAuthReq data)
    {   
        session.OnSessionAuth(data);
    }
}
