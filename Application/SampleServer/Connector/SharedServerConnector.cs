using NetworkCommon;
using SP.Engine.Runtime.Handler;
using SP.Engine.Server.Connector;
using SP.Engine.Server.Logging;

namespace SampleServer.Connector;

public class SharedServerConnector : BaseServerConnector
{
    public SharedServerConnector()
        : base("SharedServer")
    {
        Connected += OnConnected;
        Offline += OnOffline;
    }

    private void OnOffline()
    {
        LogManager.Info("Connector '{0}' offline. peerId={1}", Name, PeerId);
    }

    private void OnConnected()
    {
        LogManager.Info("Connector '{0}' connected. peerId={1}", Name, PeerId);
        var protocol = new ProtocolData.S2SS.RegisterServerReq { ServerType = "SampleServer" };
        Send(protocol);
    }

    [ProtocolMethod(Protocol.SS2S.RegisterServerAck)]
    private void OnRegisterServerAck(ProtocolData.SS2S.RegisterServerAck ack)
    {
        LogManager.Debug("ErrorCode: {0}", ack.ErrorCode);
    }
}
