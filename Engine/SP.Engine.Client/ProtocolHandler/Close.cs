using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    [ProtocolHandler(S2CEngineProtocolId.Close)]
    public class Close : BaseProtocolHandler<S2CEngineProtocolData.Close>
    {
        protected override void ExecuteProtocol(NetPeer peer, S2CEngineProtocolData.Close data)
        {
            if (peer.State == ENetPeerState.Closing)
            {
                // 클라이언트 요청으로 받은 경우, 즉시 종료함
                peer.CloseWithoutHandshake();
                return;
            }

            // 서버 요청으로 종료함
            peer.Close();
        }
    }
}
