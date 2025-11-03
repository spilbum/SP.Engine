using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.Close)]
    public class Close : BaseCommand<BaseNetPeer, S2CEngineProtocolData.Close>
    {
        protected override void ExecuteProtocol(BaseNetPeer context, S2CEngineProtocolData.Close protocol)
        {
            if (context.State == NetPeerState.Closing)
            {
                // 클라이언트 요청으로 받은 경우, 즉시 종료함
                context.CloseWithoutHandshake();
                return;
            }

            // 서버 요청으로 종료함
            context.Close();
        }
    }
}
