using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.S2C;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Protocol
{
    [ProtocolHandler(ProtocolId.S2C.MessageAck)]
    internal class MessageAckHandler : ProtocolHandlerBase<NetPeerBase, MessageAck>
    {
        protected override void ExecuteProtocol(NetPeerBase context, MessageAck protocol)
        {
            if (!context.IsConnected) return;
            context.HandleRemoteAck(protocol.NextExpectedSeq);
        }
    }
}
