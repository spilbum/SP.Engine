using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(S2CEngineProtocolId.Pong)]
    public class Pong : BaseProtocolHandler<S2CEngineProtocolData.Pong>
    {
        protected override void ExecuteProtocol(NetPeer peer, S2CEngineProtocolData.Pong data)
        {
            peer.OnPong(data.SendTimeMs, data.ServerTimeMs);
        }
    }
}
