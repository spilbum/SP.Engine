using System.Threading.Tasks;
using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.UdpHelloAck)]
    public class UdpHelloAck : BaseCommand<BaseNetPeer, S2CEngineProtocolData.UdpHelloAck>
    {
        protected override Task ExecuteCommand(BaseNetPeer context, S2CEngineProtocolData.UdpHelloAck protocol)
        {
            context.OnUdpHandshake(protocol);
            return Task.CompletedTask;
        }
    }
}
