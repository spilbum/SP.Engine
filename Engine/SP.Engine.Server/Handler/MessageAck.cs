using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Server.Handler;

[ProtocolHandler(C2SEngineProtocol.MessageAck)]
internal class MessageAck<TPeer> : BaseEngineHandler<Session<TPeer>, C2SEngineProtocolData.MessageAck>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(Session<TPeer> session, C2SEngineProtocolData.MessageAck protocol)
    {
        session.OnMessageAck(protocol.SequenceNumber);
    }
}
