using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(EngineProtocol.S2C.MessageAck)]
    public class MessageAck : BaseProtocolHandler<EngineProtocolData.S2C.MessageAck>
    {
        protected override void ExecuteProtocol(NetPeer session, EngineProtocolData.S2C.MessageAck protocol)
        {
            session.OnMessageAck(protocol.SequenceNumber);
        }
    }
}
