using Common;
using SP.Engine.Client.Command;
using SP.Engine.Runtime.Protocol;

namespace EchoServer.Connector.Command;

[ProtocolCommand(S2SProtocol.RegisterAck)]
public class RegisterAck : BaseCommand<DummyConnector, S2SProtocolData.RegisterAck>
{
    protected override void ExecuteProtocol(DummyConnector peer, S2SProtocolData.RegisterAck protocol)
    {
        
    }
}
