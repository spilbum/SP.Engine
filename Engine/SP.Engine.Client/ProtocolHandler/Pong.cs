using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(S2CEngineProtocol.Pong)]
    public class Pong : BaseProtocolHandler<S2CEngineProtocolData.Pong>
    {
        protected override void ExecuteProtocol(NetPeer session, S2CEngineProtocolData.Pong protocol)
        {
            session.OnPong(protocol.SentTime, protocol.ServerTime);
        }
    }
}
