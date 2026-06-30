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
            var nowMs = NetPeerBase.LocalElapsedTimeMs;
            var rttMs = nowMs - protocol.SentTimeMs;
            
            // RTT 지표 업데이트
            context.SetRttMs(rttMs);

            // 오프셋 계산 및 최소 RTT 필터링 적용
            var targetServerTimeMs = protocol.ServerTimeMs + rttMs / 2;
            var offset = (long)targetServerTimeMs - nowMs;
            
            context.SetServerTimeOffset(offset, rttMs);
        }
    }
}
