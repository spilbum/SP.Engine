using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.C2S;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(ProtocolId.C2S.UdpHealthCheckAck)]
internal class UdpHealthCheckAckHandler : CommandHandlerBase<Session, UdpHealthCheckAck>
{
    protected override void ExecuteCommand(Session context, UdpHealthCheckAck protocol)
    {
        if (!context.RecoverUdpHealth()) return;
        context.SendUdpStatusNotify(true);
        context.Logger.Info("Session {0} UDP restored.", context.SessionId);
    }
}
