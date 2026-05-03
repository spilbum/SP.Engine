using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.Close)]
internal class Close : BaseCommand<Session, C2SEngineProtocolData.Close>
{
    protected override void ExecuteCommand(Session session, C2SEngineProtocolData.Close protocol)
    {
        session.Logger.Debug("Received a termination request from the client. isClosing={0}", session.IsClosing);
        
        if (session.IsClosing)
        {
            // 서버 요청에 대한 응답인 경우
            session.Close(CloseReason.ServerClosing);
            return;
        }
        
        // 응답을 보내고 종료함
        session.SendClose();
        session.Close(CloseReason.ClientClosing);
    }
}
