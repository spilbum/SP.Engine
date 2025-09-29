using SP.Engine.Protocol;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(S2CEngineProtocolId.UdpHelloAck)]
    public class UdpHelloAck : BaseProtocolHandler<S2CEngineProtocolData.UdpHelloAck>
    {
        protected override void ExecuteProtocol(NetPeer peer, S2CEngineProtocolData.UdpHelloAck data)
        {
            peer.OnUdpHelloAck(data.ErrorCode);
        }
    }
}
