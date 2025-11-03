using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.MessageAck)]
    public class MessageAck : BaseCommand<BaseNetPeer, S2CEngineProtocolData.MessageAck>
    {
        protected override void ExecuteProtocol(BaseNetPeer context, S2CEngineProtocolData.MessageAck protocol)
        {
            context.OnMessageAck(protocol.SequenceNumber);
        }
    }
}
