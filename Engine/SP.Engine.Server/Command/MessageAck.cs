using System.Threading.Tasks;
using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.MessageAck)]
internal class MessageAck : BaseCommand<Session, C2SEngineProtocolData.MessageAck>
{
    protected override Task ExecuteCommand(Session session, C2SEngineProtocolData.MessageAck protocol)
    {
        session.Peer.OnMessageAck(protocol.SequenceNumber);
        return Task.CompletedTask;
    }
}
