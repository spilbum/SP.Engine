using System.Threading.Tasks;
using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.Pong)]
    public class Pong : BaseCommand<BaseNetPeer, S2CEngineProtocolData.Pong>
    {
        protected override Task ExecuteCommand(BaseNetPeer context, S2CEngineProtocolData.Pong protocol)
        {
            context.OnPong(protocol.SendTimeMs, protocol.ServerTimeMs);
            return Task.CompletedTask;
        }
    }
}
