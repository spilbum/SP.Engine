using System.Threading.Tasks;
using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.UdpKeepAlive)]
internal class UdpKeepAlive : BaseCommand<Session, C2SEngineProtocolData.UdpKeepAlive>
{
    protected override Task ExecuteCommand(Session session, C2SEngineProtocolData.UdpKeepAlive protocol)
    {
        return Task.CompletedTask;
    }
}
