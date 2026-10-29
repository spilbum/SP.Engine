using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.C2S;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Protocol;

[ProtocolHandler(ProtocolId.C2S.MessageAck)]
internal class MessageAckHandler : ProtocolHandlerBase<Session, MessageAck>
{
    protected override void ExecuteProtocol(Session session, MessageAck protocol)
    {
        var peer = session.Peer;
        peer?.MessageProcessor.AcknowledgeInFlight(protocol.NextExpectedSeq);
    }
}
