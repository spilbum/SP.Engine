using System;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(C2SEngineProtocolId.UdpHelloReq)]
internal class UdpHelloReq<TPeer> : BaseEngineHandler<Session<TPeer>, C2SEngineProtocolData.UdpHelloReq>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(Session<TPeer> session, C2SEngineProtocolData.UdpHelloReq data)
    {
        session.OnUdpHandshake(data);
    }
}
