using Common;
using GameServer.UserPeer;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameServer.Command;

[ProtocolCommand(C2GProtocol.UdpEchoReq)]
public class UdpEchoReq : BaseCommand<GamePeer, C2GProtocolData.UdpEchoReq>
{
    protected override void ExecuteCommand(GamePeer context, C2GProtocolData.UdpEchoReq protocol)
    {
        context.Send(new G2CProtocolData.UdpEchoAck { Seq = protocol.Seq, SentTicks = protocol.SentTicks, Data = protocol.Data });
    }
}
