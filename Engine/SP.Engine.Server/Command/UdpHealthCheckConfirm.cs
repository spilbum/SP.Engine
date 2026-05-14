using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.UdpHealthCheckConfirm)]
public class UdpHealthCheckConfirm : CommandBase<Session, C2SEngineProtocolData.UdpHealthCheckConfirm>
{
    protected override void ExecuteCommand(Session context, C2SEngineProtocolData.UdpHealthCheckConfirm protocol)
    {
        if (context.RecoverUdpHealth())
        {
            context.SendUdpStatusNotify(true);
            context.Logger.Info("Session {0} UDP restored.", context.SessionId);
        }
    }
}
