using SP.Engine.Runtime;
using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(EngineProtocol.S2C.SessionAuthAck)]
    public class SessionAuth : BaseProtocolHandler<EngineProtocolData.S2C.SessionAuthAck>
    {
        protected override void ExecuteProtocol(NetPeer session,EngineProtocolData.S2C.SessionAuthAck protocol)
        {
            if (protocol.ErrorCode != EEngineErrorCode.Success)
            {
                // 인증 실패로 종료 함
                session.OnError(protocol.ErrorCode);
                session.CloseWithoutHandshake();
                return;
            }
            
            // 허용되는 최대 데이터 크기 설정
            if (0 < protocol.MaxAllowedLength)
                session.SetMaxAllowedLength(protocol.MaxAllowedLength);
                    
            // 전송 타임아웃 시간 설정
            if (0 < protocol.SendTimeOutMs)
                session.SetInitialSendTimeoutMs(protocol.SendTimeOutMs);
            
            // 최대 재 전송 횟수 설정
            if (0 < protocol.MaxReSendCnt)
                session.SetMaxReSendCnt(protocol.MaxReSendCnt);

            if (protocol.UseEncryption)
            {
                session.PackOptions.UseEncryption = true;
                session.DiffieHellman.DeriveSharedKey(protocol.ServerPublicKey);
            }

            if (protocol.UseCompression)
            {
                session.PackOptions.UseCompression = true;
                session.PackOptions.CompressionThresholdPercent = protocol.CompressionThresholdPercent;
            }
            
            session.OnAuthHandshaked(protocol.PeerId, protocol.SessionId, protocol.UdpOpenPort);
        }
    }
}
