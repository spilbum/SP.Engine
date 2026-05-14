using Common;
using GameServer.UserPeer;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameServer.Command;

[ProtocolCommand(C2GProtocol.EchoReq)]
public class EchoReq : CommandBase<GamePeer, C2GProtocolData.EchoReq>
{
    protected override void ExecuteCommand(GamePeer peer, C2GProtocolData.EchoReq protocol)
    {
        peer.Send(new G2CProtocolData.EchoAck { Seq = protocol.Seq, SentTicks = protocol.SentTicks });
    }
}
