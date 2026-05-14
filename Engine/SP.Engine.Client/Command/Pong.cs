using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.Pong)]
    public class Pong : CommandBase<NetPeerBase, S2CEngineProtocolData.Pong>
    {
        protected override void ExecuteCommand(NetPeerBase context, S2CEngineProtocolData.Pong protocol)
        {
            var nowMs = NetPeerBase.NetworkTimeMs;
            var rttMs = nowMs - protocol.ClientSendTimeMs;
            context.SetRttMs(rttMs);

            var estimatedServerNetworkTime = protocol.ServerTimeMs + rttMs / 2;
            var offset = (long)estimatedServerNetworkTime - nowMs;
            context.SetServerTimeOffset(offset);
        }
    }
}
