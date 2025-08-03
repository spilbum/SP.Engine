using Common;
using SP.Engine.Runtime.Handler;
using SP.Engine.Server.ProtocolHandler;

namespace TestServer.ProtocolHandler;

[ProtocolHandler(C2SProtocol.UdpEchoReq)]
public class EchoReq : BaseProtocolHandler<NetPeer, C2SProtocolData.UdpEchoReq>
{
    protected override void ExecuteProtocol(NetPeer peer, C2SProtocolData.UdpEchoReq data)
    {
        peer.Send(new S2CProtocolData.UdpEchoAck { SentTime = data.SendTime, Data = data.Data});
    }
}
