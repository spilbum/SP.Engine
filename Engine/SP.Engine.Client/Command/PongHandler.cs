using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.S2C;
using SP.Engine.Runtime.Command;

namespace SP.Engine.Client.Command
{
    [CommandHandler(ProtocolId.S2C.Pong)]
    internal class PongHandler : CommandHandlerBase<NetPeerBase, Pong>
    {
        protected override void ExecuteCommand(NetPeerBase context, Pong protocol)
        {
            var nowMs = NetPeerBase.NetworkTimeMs;
            var rttMs = nowMs - protocol.SentTimeMs;
            context.SetRttMs(rttMs);

            var estimatedServerNetworkTime = protocol.ServerTimeMs + rttMs / 2;
            var offset = (long)estimatedServerNetworkTime - nowMs;
            context.SetServerTimeOffset(offset);
        }
    }
}
