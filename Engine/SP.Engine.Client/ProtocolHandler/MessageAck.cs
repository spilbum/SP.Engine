using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(S2CEngineProtocolId.MessageAck)]
    public class MessageAck : BaseProtocolHandler<S2CEngineProtocolData.MessageAck>
    {
        protected override void ExecuteProtocol(NetPeer peer, S2CEngineProtocolData.MessageAck data)
        {
            peer.OnMessageAck(data.SequenceNumber);
        }
    }
}
