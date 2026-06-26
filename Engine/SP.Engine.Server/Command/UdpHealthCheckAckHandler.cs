using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.C2S;
using SP.Engine.Runtime.Command;

namespace SP.Engine.Server.Command;

[CommandHandler(ProtocolId.C2S.UdpHealthCheckAck)]
internal class UdpHealthCheckAckHandler : CommandHandlerBase<Session, UdpHealthCheckAck>
{
    protected override void ExecuteCommand(Session context, UdpHealthCheckAck protocol)
    {
        if (!context.RecoverUdpHealth()) return;
        context.SendUdpStatusNotify(true);
        context.Logger.Info("Session {0} UDP restored.", context.SessionId);
    }
}
