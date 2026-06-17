using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.C2S;
using SP.Engine.Common.Protocol.S2C;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(ProtocolId.S2C.UdpHealthCheckReq)]
    internal class UdpHealthCheckReqHandler : CommandHandlerBase<NetPeerBase, UdpHealthCheckReq>
    {
        protected override void ExecuteCommand(NetPeerBase context, UdpHealthCheckReq protocol)
        {
            context.InternalSend(new UdpHealthCheckAck());
        }
    }
}
