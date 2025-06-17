using SP.Engine.Protocol;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(EngineProtocol.C2S.MessageAck)]
internal class MessageAck<TPeer> : BaseEngineHandler<ClientSession<TPeer>, EngineProtocolData.C2S.MessageAck>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(ClientSession<TPeer> session, EngineProtocolData.C2S.MessageAck protocol)
    {
        session.Peer.OnMessageAck(protocol.SequenceNumber);
    }
}
