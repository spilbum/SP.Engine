
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(EngineProtocol.C2S.UdpHelloReq)]
internal class UdpHelloReq<TPeer> : BaseEngineHandler<ClientSession<TPeer>, EngineProtocolData.C2S.UdpHelloReq>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(ClientSession<TPeer> session, EngineProtocolData.C2S.UdpHelloReq protocol)
    {
        var resposne = new EngineProtocolData.S2C.UdpHelloAck { ErrorCode = EEngineErrorCode.Unknown };
        if (session.SessionId != protocol.SessionId ||
            session.Peer.PeerId != protocol.PeerId)
        {
            resposne.ErrorCode = EEngineErrorCode.Invalid;
            session.Send(resposne);
            return;
        }
        
        resposne.ErrorCode = EEngineErrorCode.Success;
        session.Send(resposne);
    }
}
