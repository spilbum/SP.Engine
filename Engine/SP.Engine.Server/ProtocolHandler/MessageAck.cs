using SP.Engine.Protocol;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolCommand(C2SEngineProtocolId.MessageAck)]
internal class MessageAck : BaseCommand<Session, C2SEngineProtocolData.MessageAck>
{
    protected override void ExecuteProtocol(Session session, C2SEngineProtocolData.MessageAck protocol)
    {
        session.Peer.OnMessageAck(protocol.SequenceNumber);
    }
}
