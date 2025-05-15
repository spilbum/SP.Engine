using NetworkCommon;
using SharedServer.Peer;
using SP.Engine.Runtime.Handler;
using SP.Engine.Server.Handler;

namespace SharedServer.ProtocolHandler;

[ProtocolHandler(Protocol.S2SS.RegisterServerReq)]
public class RegisterServer : BaseProtocolHandler<ServerPeer, ProtocolData.S2SS.RegisterServerReq>
{
    protected override void ExecuteProtocol(ServerPeer peer, ProtocolData.S2SS.RegisterServerReq protocol)
    {
        ServerPeer? serverPeer = null;
        switch (protocol.ServerType)
        {
            case "SampleServer":
                serverPeer = new EchoServerPeer(peer);
                break;
            default:
            {
                peer.Send(new ProtocolData.SS2S.RegisterServerAck { ErrorCode = -3 });
                break;
            }
                
        }

        var response = serverPeer?.ExecuteProtocol(protocol);
        if (response != null)
            peer.Send(response);
    }
}
