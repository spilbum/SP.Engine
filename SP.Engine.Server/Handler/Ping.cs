using SP.Engine.Runtime.Handler;
using SP.Protocol;

namespace SP.Engine.Server.Handler;

[ProtocolHandler(C2SEngineProtocol.Ping)]
internal class Ping<TPeer> : BaseEngineHandler<Session<TPeer>, C2SEngineProtocol.Data.Ping>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(Session<TPeer> session, C2SEngineProtocol.Data.Ping protocol)
    {
        session.OnPing(protocol.SendTime, protocol.LatencyAverageMs, protocol.LatencyStandardDeviationMs);
    }
}
