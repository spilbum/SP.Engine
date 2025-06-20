using SP.Engine.Protocol;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(EngineProtocol.C2S.Ping)]
internal class Ping<TPeer> : BaseEngineHandler<ClientSession<TPeer>, EngineProtocolData.C2S.Ping>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(ClientSession<TPeer> session, EngineProtocolData.C2S.Ping protocol)
    {
        session.Peer.OnPing(protocol.RawRttMs, protocol.AvgRttMs, protocol.JitterMs, protocol.PacketLossRate);
        session.SendPong(protocol.SendTimeMs);
    }
}
