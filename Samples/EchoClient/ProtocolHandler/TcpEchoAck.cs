using Common;
using SP.Engine.Client;
using SP.Engine.Client.ProtocolHandler;
using SP.Engine.Runtime.Protocol;

namespace EchoClient.ProtocolHandler;

[ProtocolHandler(S2CProtocol.TcpEchoAck)]
public class TcpEchoAck : BaseProtocolHandler<S2CProtocolData.TcpEchoAck>
{
    protected override void ExecuteProtocol(NetPeer peer, S2CProtocolData.TcpEchoAck protocol)
    {
        //peer.Logger.Debug("[TcpEchoAck] Received");
    }
}
