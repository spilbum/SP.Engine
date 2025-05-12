using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(S2CEngineProtocol.MessageAck)]
    public class MessageAck : BaseProtocolHandler<S2CEngineProtocolData.MessageAck>
    {
        protected override void ExecuteProtocol(NetPeer session, S2CEngineProtocolData.MessageAck protocol)
        {
            session.OnMessageAck(protocol.SequenceNumber);
        }
    }
}
