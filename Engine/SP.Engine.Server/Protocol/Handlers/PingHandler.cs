using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.C2S;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Protocol;

[ProtocolHandler(ProtocolId.C2S.Ping)]
internal class PingHandler : ProtocolHandlerBase<Session, Ping>
{
    protected override void ExecuteProtocol(Session session, Ping protocol)
    {
        var peer = session.Peer;
        if (peer != null)
        {
            peer.MessageProcessor.AddRtoSample(protocol.RttMs);
            peer.AvgRttMs = protocol.AvgRttMs;
            peer.JitterMs = protocol.JitterMs;
        }

        session.SendPong(protocol.SendTimeMs);
    }
}
