using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.C2S;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(ProtocolId.C2S.Ping)]
internal class PingHandler : CommandHandlerBase<Session, Ping>
{
    protected override void ExecuteCommand(Session session, Ping protocol)
    {
        session.Peer?.RecordPingData(protocol.RttMs, protocol.AvgRttMs, protocol.JitterMs);
        session.SendPong(protocol.SendTimeMs);
    }
}
