using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.Ping)]
internal class Ping : CommandBase<Session, C2SEngineProtocolData.Ping>
{
    protected override void ExecuteCommand(Session session, C2SEngineProtocolData.Ping protocol)
    {
        session.Peer?.RecordPingData(protocol.RawRttMs, protocol.AvgRttMs, protocol.JitterMs);
        session.SendPong(protocol.SendTimeMs);
    }
}
