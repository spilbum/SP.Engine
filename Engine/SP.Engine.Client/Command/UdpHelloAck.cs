using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.UdpHelloAck)]
    public class UdpHelloAck : CommandBase<NetPeerBase, S2CEngineProtocolData.UdpHelloAck>
    {
        protected override void ExecuteCommand(NetPeerBase context, S2CEngineProtocolData.UdpHelloAck protocol)
        {
            if (protocol.Result != UdpHandshakeResult.Ok)
            {
                context.UdpHandshakeFailed();
                context.Logger.Error("Peer {0} UDP handshake failed: {1}", context.PeerId, protocol.Result);
                return;
            }
            
            context.UdpHandshakeCompleted(protocol.Mtu);
        }
    }
}
