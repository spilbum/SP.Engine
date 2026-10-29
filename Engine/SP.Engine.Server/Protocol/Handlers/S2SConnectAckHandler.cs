using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.S2S;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.S2S;

namespace SP.Engine.Server.Protocol;

[ProtocolHandler(ProtocolId.S2S.S2SConnectAck)]
internal class S2SConnectAckHandler : ProtocolHandlerBase<S2SClient, S2SConnectAck>
{
    protected override void ExecuteProtocol(S2SClient context, S2SConnectAck protocol)
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
