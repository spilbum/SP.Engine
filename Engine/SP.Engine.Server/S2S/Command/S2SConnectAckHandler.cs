using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.S2S;
using SP.Engine.Runtime.Command;

namespace SP.Engine.Server.S2S.Command;

[CommandHandler(ProtocolId.S2S.S2SConnectAck)]
internal class S2SConnectAckHandler : CommandHandlerBase<S2SClient, S2SConnectAck>
{
    protected override void ExecuteCommand(S2SClient context, S2SConnectAck protocol)
    {
        switch (protocol.Result)
        {
            case S2SConnectResult.AlreadyConnected:
                // 이미 연결됨 
                break;
            case S2SConnectResult.Success:
                // 연결 성공
                context.S2SConnectCompleted();
                break;
            case S2SConnectResult.InternalError:
            default:
                context.Close();
                break;
        }
    }
}
