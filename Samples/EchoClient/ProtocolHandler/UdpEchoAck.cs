using Common;
using SP.Engine.Client;
using SP.Engine.Client.ProtocolHandler;
using SP.Engine.Runtime.Protocol;

namespace EchoClient.ProtocolHandler;

[ProtocolHandler(S2CProtocol.UdpEchoAck)]
public class UdpEchoAck : BaseProtocolHandler<S2CProtocolData.UdpEchoAck>
{
    protected override void ExecuteProtocol(NetPeer peer, S2CProtocolData.UdpEchoAck protocol)
    {
        //peer.Logger.Debug("[UdpEchoAck] Received");
    }
}
