using SP.Engine.Protocol;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(EngineProtocol.C2S.UdpKeepAlive)]
internal class UdpKeepAlive<TPeer> : BaseEngineHandler<ClientSession<TPeer>, EngineProtocolData.C2S.UdpKeepAlive> 
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(ClientSession<TPeer> session, EngineProtocolData.C2S.UdpKeepAlive protocol)
    {
    }
}
