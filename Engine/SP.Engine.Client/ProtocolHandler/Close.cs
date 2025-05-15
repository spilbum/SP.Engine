using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(EngineProtocol.S2C.Close)]
    public class Close : BaseProtocolHandler<EngineProtocolData.S2C.Close>
    {
        protected override void ExecuteProtocol(NetPeer session, EngineProtocolData.S2C.Close protocol)
        {
            if (session.State == ENetPeerState.Closing)
            {
                // 클라이언트 요청으로 받은 경우, 즉시 종료함
                session.CloseWithoutHandshake();
                return;
            }

            // 서버 요청으로 종료함
            session.Close();
        }
    }
}
