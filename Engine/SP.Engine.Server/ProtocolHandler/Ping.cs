using SP.Engine.Protocol;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(C2SEngineProtocolId.Ping)]
internal class Ping<TPeer> : BaseEngineHandler<Session<TPeer>, C2SEngineProtocolData.Ping>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(Session<TPeer> session, C2SEngineProtocolData.Ping data)
    {
        session.Peer.OnPing(data.RawRttMs, data.AvgRttMs, data.JitterMs, data.PacketLossRate);
        session.SendPong(data.SendTimeMs);
    }
}
