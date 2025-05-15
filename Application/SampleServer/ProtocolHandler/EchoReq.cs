using NetworkCommon;
using SP.Engine.Runtime.Handler;
using SP.Engine.Server.Handler;

namespace SampleServer.ProtocolHandler;

[ProtocolHandler(Protocol.C2ES.EchoReq)]
public class EchoReq : BaseProtocolHandler<UserPeer, ProtocolData.C2S.EchoReq>
{
    protected override void ExecuteProtocol(UserPeer peer, ProtocolData.C2S.EchoReq protocol)
    {
        peer.Logger.Debug("Echo message received: {0}", protocol.Message);
        peer.Send(new ProtocolData.S2C.EchoAck { Message = protocol.Message });
    }
}
