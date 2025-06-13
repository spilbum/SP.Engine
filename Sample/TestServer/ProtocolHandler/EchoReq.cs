using Common;
using SP.Engine.Runtime.Handler;
using SP.Engine.Server.ProtocolHandler;

namespace TestServer.ProtocolHandler;

[ProtocolHandler(Protocol.C2S.EchoReq)]
public class EchoReq : BaseProtocolHandler<NetPeer, ProtocolData.C2S.EchoReq>
{
    protected override void ExecuteProtocol(NetPeer peer, ProtocolData.C2S.EchoReq protocol)
    {
        peer.Send(new ProtocolData.S2C.EchoAck { Str = protocol.Str, Bytes = protocol.Bytes, SentTime = protocol.SendTime });
    }
}
