using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.SessionAuthAck)]
    public class SessionAuth : BaseCommand<BaseNetPeer, S2CEngineProtocolData.SessionAuthAck>
    {
        protected override void ExecuteProtocol(BaseNetPeer context, S2CEngineProtocolData.SessionAuthAck protocol)
        {
            context.OnAuthHandshake(protocol);
        }
    }
}
