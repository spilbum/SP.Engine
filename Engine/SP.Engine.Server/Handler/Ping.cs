using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Server.Handler;

[ProtocolHandler(EngineProtocol.C2S.Ping)]
internal class Ping<TPeer> : BaseEngineHandler<Session<TPeer>, EngineProtocolData.C2S.Ping>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(Session<TPeer> session, EngineProtocolData.C2S.Ping protocol)
    {
        session.OnPing(protocol.SendTime, protocol.LatencyAverageMs, protocol.LatencyStandardDeviationMs);
    }
}
