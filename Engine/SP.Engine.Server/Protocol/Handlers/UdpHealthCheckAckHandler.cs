using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.C2S;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Protocol;

[ProtocolHandler(ProtocolId.C2S.UdpHealthCheckAck)]
internal class UdpHealthCheckAckHandler : ProtocolHandlerBase<Session, UdpHealthCheckAck>
{
    protected override void ExecuteProtocol(Session context, UdpHealthCheckAck protocol)
    {
        if (!context.RecoverUdpHealth()) return;
        context.SendUdpStatusNotify(true);
        context.Logger.Info("Session {0} UDP restored.", context.SessionId);
    }
}
