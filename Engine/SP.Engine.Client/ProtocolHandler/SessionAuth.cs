using SP.Engine.Protocol;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(S2CEngineProtocolId.SessionAuthAck)]
    public class SessionAuth : BaseProtocolHandler<S2CEngineProtocolData.SessionAuthAck>
    {
        protected override void ExecuteProtocol(NetPeer peer, S2CEngineProtocolData.SessionAuthAck protocol)
        {
            peer.OnAuthHandshake(protocol);
        }
    }
}
