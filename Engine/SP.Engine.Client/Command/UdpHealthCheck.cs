using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.UdpHealthCheck)]
    public class UdpHealthCheck : CommandBase<NetPeerBase, S2CEngineProtocolData.UdpHealthCheck>
    {
        protected override void ExecuteCommand(NetPeerBase context, S2CEngineProtocolData.UdpHealthCheck protocol)
        {
            context.InternalSend(new C2SEngineProtocolData.UdpHealthCheckConfirm());
        }
    }
}
