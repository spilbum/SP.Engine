using Common;
using SP.Engine.Runtime.Handler;
using SP.Engine.Server.ProtocolHandler;

namespace TestServer.ProtocolHandler;

[ProtocolHandler(Protocol.C2S.Echo)]
public class Echo : BaseProtocolHandler<NetPeer, ProtocolData.C2S.Echo>
{
    protected override void ExecuteProtocol(NetPeer peer, ProtocolData.C2S.Echo protocol)
    {
        peer.Logger.Info("Message received: {0}, bytes={1}, time={2}", protocol.Str, protocol.Bytes?.Length, protocol.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        peer.Send(new ProtocolData.S2C.Echo { Str = protocol.Str, Bytes = protocol.Bytes, Time = protocol.Time });
    }
}
