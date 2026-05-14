using SP.Engine.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.UdpStatusNotify)]
    public class UdpStatusNotify : CommandBase<NetPeerBase, S2CEngineProtocolData.UdpStatusNotify>
    {
        protected override void ExecuteCommand(NetPeerBase context, S2CEngineProtocolData.UdpStatusNotify protocol)
        {
            if (protocol.IsEnabled)
            {
                if (context.EnableUdp())
                    context.Logger.Warn("Peer {0} UDP enabled.", context.PeerId);
            }
            else
            {
                if (context.DisableUdp())
                    context.Logger.Warn("Peer {0} UDP disabled.", context.PeerId);
            }
        }
    }
}
