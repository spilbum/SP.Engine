using SP.Engine.Runtime.Handler;
using SP.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(S2CEngineProtocol.MessageAck)]
    public class MessageAck : BaseProtocolHandler<S2CEngineProtocol.Data.MessageAck>
    {
        protected override void ExecuteProtocol(NetPeer session, S2CEngineProtocol.Data.MessageAck protocol)
        {
            session.OnMessageAck(protocol.SequenceNumber);
        }
    }
}
