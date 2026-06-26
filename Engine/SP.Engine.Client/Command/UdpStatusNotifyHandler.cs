using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.S2C;
using SP.Engine.Runtime.Command;

namespace SP.Engine.Client.Command
{
    [CommandHandler(ProtocolId.S2C.UdpStatusNotify)]
    internal class UdpStatusNotifyHandler : CommandHandlerBase<NetPeerBase, UdpStatusNotify>
    {
        protected override void ExecuteCommand(NetPeerBase context, UdpStatusNotify protocol)
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
