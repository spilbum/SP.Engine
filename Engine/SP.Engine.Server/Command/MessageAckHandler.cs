using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.C2S;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(ProtocolId.C2S.MessageAck)]
internal class MessageAckHandler : CommandHandlerBase<Session, MessageAck>
{
    protected override void ExecuteCommand(Session session, MessageAck protocol)
    {
        session.Peer?.HandleRemoteAck(protocol.NextExpectedSeq);
    }
}
