using SP.Engine.Runtime.Handler;
using SP.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(S2CEngineProtocol.Pong)]
    public class Pong : BaseProtocolHandler<S2CEngineProtocol.Data.Pong>
    {
        protected override void ExecuteProtocol(NetPeer session, S2CEngineProtocol.Data.Pong protocol)
        {
            session.OnPong(protocol.SentTime, protocol.ServerTime);
        }
    }
}
