using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.UdpHealthCheckConfirm)]
public class UdpHealthCheckConfirm : BaseCommand<Session, C2SEngineProtocolData.UdpHealthCheckConfirm>
{
    protected override void ExecuteCommand(Session context, C2SEngineProtocolData.UdpHealthCheckConfirm protocol)
    {
        context.OnUdpHealthCheckConfirm();
    }
}
