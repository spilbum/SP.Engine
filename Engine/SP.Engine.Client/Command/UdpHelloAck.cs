using SP.Engine.Protocol;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.UdpHelloAck)]
    public class UdpHelloAck : BaseCommand<BaseNetPeer, S2CEngineProtocolData.UdpHelloAck>
    {
        protected override void ExecuteProtocol(BaseNetPeer peer, S2CEngineProtocolData.UdpHelloAck protocol)
        {
            peer.OnUdpHandshake(protocol);
        }
    }
}
