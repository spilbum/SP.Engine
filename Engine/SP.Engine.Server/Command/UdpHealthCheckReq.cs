using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.UdpHealthCheckReq)]
internal class UdpHealthCheckReq : BaseCommand<Session, C2SEngineProtocolData.UdpHealthCheckReq>
{
    protected override void ExecuteCommand(Session session, C2SEngineProtocolData.UdpHealthCheckReq protocol)
    {
        session.InvalidateUdpHealth(session.Config.Network.MaxUdpHealthFail);
        session.InternalSend(new S2CEngineProtocolData.UdpHealthCheckAck());
    }
}
