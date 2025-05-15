using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(EngineProtocol.S2C.Pong)]
    public class Pong : BaseProtocolHandler<EngineProtocolData.S2C.Pong>
    {
        protected override void ExecuteProtocol(NetPeer session, EngineProtocolData.S2C.Pong protocol)
        {
            session.OnPong(protocol.SentTime, protocol.ServerTime);
        }
    }
}
