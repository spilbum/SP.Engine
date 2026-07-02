using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.C2S;
using SP.Engine.Runtime.Command;

namespace SP.Engine.Server.Command;

[CommandHandler(ProtocolId.C2S.MessageAck)]
internal class MessageAckHandler : CommandHandlerBase<Session, MessageAck>
{
    protected override void ExecuteCommand(Session session, MessageAck protocol)
    {
        var peer = session.Peer;
        peer?.MessageProcessor.AcknowledgeInFlight(protocol.NextExpectedSeq);
    }
}
