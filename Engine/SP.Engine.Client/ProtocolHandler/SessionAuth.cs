using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(S2CEngineProtocolId.SessionAuthAck)]
    public class SessionAuth : BaseProtocolHandler<S2CEngineProtocolData.SessionAuthAck>
    {
        protected override void ExecuteProtocol(NetPeer peer, S2CEngineProtocolData.SessionAuthAck data)
        {
            peer.OnAuthHandshake(data);
        }
    }
}
