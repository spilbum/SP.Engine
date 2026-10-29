using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.C2S;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Protocol;

[ProtocolHandler(ProtocolId.C2S.CloseCmd)]
internal class CloseCmdHandler : ProtocolHandlerBase<Session, CloseCmd>
{
    protected override void ExecuteProtocol(Session session, CloseCmd protocol)
    {
        session.Logger.Debug("Received a termination request from the client. isClosing={0}", session.IsClosing);
        
        if (session.IsClosing)
        {
            // 서버 요청에 대한 응답인 경우
            session.Close(session.CloseReason != CloseReason.Unknown ? session.CloseReason : CloseReason.ServerClosing);
            return;
        }
        
        // 응답을 보내고 종료함
        session.SendClose();
        session.Close(CloseReason.ClientClosing);
    }
}
