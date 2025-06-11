using SP.Engine.Protocol;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(EngineProtocol.S2C.UdpHelloAck)]
    public class UdpHelloAck : BaseProtocolHandler<EngineProtocolData.S2C.UdpHelloAck>
    {
        protected override void ExecuteProtocol(NetPeer session, EngineProtocolData.S2C.UdpHelloAck protocol)
        {
            session.Logger.Info("UDP hello ack: {0}", protocol.ErrorCode);
        }
    }
}
