using SP.Engine.Protocol;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.Ping)]
internal class Ping : BaseCommand<Session, C2SEngineProtocolData.Ping>
{
    protected override void ExecuteProtocol(Session session, C2SEngineProtocolData.Ping protocol)
    {
        session.Peer.OnPing(protocol.RawRttMs, protocol.AvgRttMs, protocol.JitterMs, protocol.PacketLossRate);
        session.SendPong(protocol.SendTimeMs);
    }
}
