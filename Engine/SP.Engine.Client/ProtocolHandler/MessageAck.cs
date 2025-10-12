using SP.Engine.Protocol;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(S2CEngineProtocolId.MessageAck)]
    public class MessageAck : BaseProtocolHandler<S2CEngineProtocolData.MessageAck>
    {
        protected override void ExecuteProtocol(NetPeer peer, S2CEngineProtocolData.MessageAck protocol)
        {
            peer.OnMessageAck(protocol.SequenceNumber);
        }
    }
}
