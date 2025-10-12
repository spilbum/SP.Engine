using SP.Engine.Protocol;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(S2CEngineProtocolId.Pong)]
    public class Pong : BaseProtocolHandler<S2CEngineProtocolData.Pong>
    {
        protected override void ExecuteProtocol(NetPeer peer, S2CEngineProtocolData.Pong protocol)
        {
            peer.OnPong(protocol.SendTimeMs, protocol.ServerTimeMs);
        }
    }
}
