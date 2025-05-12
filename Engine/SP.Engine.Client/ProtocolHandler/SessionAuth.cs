using SP.Engine.Runtime;
using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(S2CEngineProtocol.SessionAuthAck)]
    public class SessionAuth : BaseProtocolHandler<S2CEngineProtocolData.SessionAuthAck>
    {
        protected override void ExecuteProtocol(NetPeer session,S2CEngineProtocolData.SessionAuthAck protocol)
        {
            if (protocol.ErrorCode != EEngineErrorCode.Success)
            {
                // 인증 실패로 종료 함
                session.OnError(protocol.ErrorCode);
                session.Close();
                return;
            }
            
            // 전송 타임아웃 시간 설정
            if (0 < protocol.SendTimeOutMs)
                session.SetSendTimeOutMs(protocol.SendTimeOutMs);
            
            // 최대 재 전송 횟수 설정
            if (0 < protocol.MaxReSendCnt)
                session.SetMaxReSendCnt(protocol.MaxReSendCnt);

            session.OnOpened(protocol.PeerId, protocol.SessionId, protocol.ServerPublicKey);
        }
    }
}
