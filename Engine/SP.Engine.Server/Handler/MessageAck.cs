using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Server.Handler;

[ProtocolHandler(EngineProtocol.C2S.MessageAck)]
internal class MessageAck<TPeer> : BaseEngineHandler<Session<TPeer>, EngineProtocolData.C2S.MessageAck>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(Session<TPeer> session, EngineProtocolData.C2S.MessageAck protocol)
    {
        session.OnMessageAck(protocol.SequenceNumber);
    }
}
