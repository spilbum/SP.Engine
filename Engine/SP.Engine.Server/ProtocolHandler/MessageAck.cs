using SP.Engine.Protocol;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(C2SEngineProtocolId.MessageAck)]
internal class MessageAck<TPeer> : BaseEngineHandler<ClientSession<TPeer>, C2SEngineProtocolData.MessageAck>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(ClientSession<TPeer> session, C2SEngineProtocolData.MessageAck data)
    {
        session.Peer.OnMessageAck(data.SequenceNumber);
    }
}
