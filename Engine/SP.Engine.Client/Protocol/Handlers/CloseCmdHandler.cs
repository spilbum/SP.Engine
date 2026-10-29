using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.S2C;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Protocol
{
    [ProtocolHandler(ProtocolId.S2C.CloseCmd)]
    internal class CloseCmdHandler : ProtocolHandlerBase<NetPeerBase, CloseCmd>
    {
        protected override void ExecuteProtocol(NetPeerBase context, CloseCmd protocol)
        {
            if (context.State == NetPeerState.Closing)
            {
                // 클라이언트 요청으로 받은 경우, 즉시 종료함
                context.Logger.Debug("Ack close handshake. state: {0}", context.State);
                context.CloseWithoutHandshake();
                return;
            }
            
            // 서버 요청으로 종료함
            context.Close();
        }
    }
}
