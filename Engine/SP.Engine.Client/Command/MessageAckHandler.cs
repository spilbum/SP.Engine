using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.S2C;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(ProtocolId.S2C.MessageAck)]
    internal class MessageAckHandler : CommandHandlerBase<NetPeerBase, MessageAck>
    {
        protected override void ExecuteCommand(NetPeerBase context, MessageAck protocol)
        {
            if (!context.IsConnected) return;
            context.HandleRemoteAck(protocol.NextExpectedSeq);
        }
    }
}
