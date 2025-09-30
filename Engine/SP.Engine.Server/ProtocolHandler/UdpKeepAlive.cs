using SP.Engine.Protocol;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(C2SEngineProtocolId.UdpKeepAlive)]
internal class UdpKeepAlive<TPeer> : BaseEngineHandler<Session<TPeer>, C2SEngineProtocolData.UdpKeepAlive>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(Session<TPeer> session, C2SEngineProtocolData.UdpKeepAlive data)
    {
    }
}
