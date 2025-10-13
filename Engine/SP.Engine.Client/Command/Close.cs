using SP.Engine.Protocol;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.Close)]
    public class Close : BaseCommand<BaseNetPeer, S2CEngineProtocolData.Close>
    {
        protected override void ExecuteProtocol(BaseNetPeer peer, S2CEngineProtocolData.Close protocol)
        {
            if (peer.State == NetPeerState.Closing)
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
