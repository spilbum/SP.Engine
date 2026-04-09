using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.UdpHealthCheckAck)]
    public class UdpHealthCheckAck : BaseCommand<BaseNetPeer, S2CEngineProtocolData.UdpHealthCheckAck>
    {
        protected override void ExecuteCommand(BaseNetPeer context, S2CEngineProtocolData.UdpHealthCheckAck protocol)
        {
            context.OnUdpHealthCheckAck();
        }
    }
}
