using SP.Engine.Protocol;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.Pong)]
    public class Pong: BaseCommand<BaseNetPeer, S2CEngineProtocolData.Pong>
    {
        protected override void ExecuteProtocol(BaseNetPeer peer, S2CEngineProtocolData.Pong protocol)
        {
            peer.OnPong(protocol.SendTimeMs, protocol.ServerTimeMs);
        }
    }
}
