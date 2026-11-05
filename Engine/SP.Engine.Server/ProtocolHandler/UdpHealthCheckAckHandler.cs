using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.C2S;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Logging;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(ProtocolId.C2S.UdpHealthCheckAck)]
internal class UdpHealthCheckAckHandler : ProtocolHandlerBase<Session, UdpHealthCheckAck>
{
    protected override void ExecuteProtocol(Session context, UdpHealthCheckAck protocol)
    {
        if (!context.RecoverUdpHealth()) return;
        context.SendUdpStatusNotify(true);
        Logger.Info($"Session {context.SessionId} UDP restored.");
    }
}
