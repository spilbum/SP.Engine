using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(C2SEngineProtocolId.UdpHelloReq)]
internal class UdpHelloReq<TPeer> : BaseEngineHandler<Session<TPeer>, C2SEngineProtocolData.UdpHelloReq>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(Session<TPeer> session, C2SEngineProtocolData.UdpHelloReq data)
    {
        var resposne = new S2CEngineProtocolData.UdpHelloAck { ErrorCode = EngineErrorCode.Unknown };
        if (session.SessionId != data.SessionId ||
            session.Peer.Id != data.PeerId)
        {
            resposne.ErrorCode = EngineErrorCode.Invalid;
            session.InternalSend(resposne);
            return;
        }

        session.Logger.Debug("UDP hello - {0} ({1}) - {2}", session.Peer.Id, session.SessionId, session.UdpSocket.RemoteEndPoint);
        
        resposne.ErrorCode = EngineErrorCode.Success;
        session.UdpSocket.SetMtu(data.Mtu);
        session.InternalSend(resposne);
    }
}
