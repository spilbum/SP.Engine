using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.UdpHelloReq)]
internal class UdpHelloReq : BaseCommand<Session, C2SEngineProtocolData.UdpHelloReq>
{
    protected override void ExecuteProtocol(Session session, C2SEngineProtocolData.UdpHelloReq protocol)
    {
        session.OnUdpHandshake(protocol);
    }
}
