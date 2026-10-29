using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.S2C;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Protocol
{
    [ProtocolHandler(ProtocolId.S2C.UdpStatusNotify)]
    internal class UdpStatusNotifyHandler : ProtocolHandlerBase<NetPeerBase, UdpStatusNotify>
    {
        protected override void ExecuteProtocol(NetPeerBase context, UdpStatusNotify protocol)
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
