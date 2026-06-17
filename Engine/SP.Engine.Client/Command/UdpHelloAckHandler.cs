using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.S2C;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(ProtocolId.S2C.UdpHelloAck)]
    internal class UdpHelloAckHandler : CommandHandlerBase<NetPeerBase, UdpHelloAck>
    {
        protected override void ExecuteCommand(NetPeerBase context, UdpHelloAck protocol)
        {
            if (protocol.Result != UdpHelloResult.Ok)
            {
                context.UdpHandshakeFailed();
                context.Logger.Error("Peer {0} UDP handshake failed: {1}", context.PeerId, protocol.Result);
                return;
            }
            
            context.UdpHandshakeCompleted(protocol.Mtu);
        }
    }
}
