using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.UdpHealthCheckConfirm)]
public class UdpHealthCheckConfirm : BaseCommand<Session, C2SEngineProtocolData.UdpHealthCheckConfirm>
{
    protected override void ExecuteCommand(Session session, C2SEngineProtocolData.UdpHealthCheckConfirm protocol)
    {
        session.RecoverUdpHealth();
    }
}
