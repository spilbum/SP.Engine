using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.C2S;
using SP.Engine.Common.Protocol.S2C;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Protocol
{
    [ProtocolHandler(ProtocolId.S2C.UdpHealthCheckReq)]
    internal class UdpHealthCheckReqHandler : ProtocolHandlerBase<NetPeerBase, UdpHealthCheckReq>
    {
        protected override void ExecuteProtocol(NetPeerBase context, UdpHealthCheckReq protocol)
        {
            context.InternalSend(new UdpHealthCheckAck());
        }
    }
}
