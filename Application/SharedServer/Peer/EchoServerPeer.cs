using NetworkCommon;
using SP.Engine.Runtime.Protocol;

namespace SharedServer.Peer;

public class EchoServerPeer(ServerPeer peer) : ServerPeer(peer)
{
    public override IProtocolData? ExecuteProtocol(IProtocolData protocol)
    {
        switch (protocol.ProtocolId)
        {
            case Protocol.S2SS.RegisterServerReq:
                return HandleRegisterServerReq((ProtocolData.S2SS.RegisterServerReq)protocol);
            default:
                Logger.Error("Unknown protocol: {0}", protocol.ProtocolId);
                return null;
        }
    }

    private ProtocolData.SS2S.RegisterServerAck HandleRegisterServerReq(ProtocolData.S2SS.RegisterServerReq protocol)
    {
        var resposne = new ProtocolData.SS2S.RegisterServerAck { ErrorCode = -1 };

        ServerType = protocol.ServerType;
        if (!SharedServer.Instance.AddPeer(this))
        {
            resposne.ErrorCode = -2;
            return resposne;
        }
        
        resposne.ErrorCode = 0;
        return resposne;
    }
}
