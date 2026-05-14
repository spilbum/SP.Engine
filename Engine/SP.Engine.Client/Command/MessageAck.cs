using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.MessageAck)]
    public class MessageAck : CommandBase<NetPeerBase, S2CEngineProtocolData.MessageAck>
    {
        protected override void ExecuteCommand(NetPeerBase context, S2CEngineProtocolData.MessageAck protocol)
        {
            context.MessageProcessor.AcknowledgeInFlight(protocol.AckNumber);
        }
    }
}
