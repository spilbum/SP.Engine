using SP.Engine.Runtime.Networking;

namespace SP.Engine.Common.Protocol.S2S
{
    [ProtocolData(ProtocolId.S2S.S2SConnectReq)]
    internal class S2SConnectReq : ProtocolDataBase<S2SConnectReq>
    {
        public string? Category { get; set; }
    }

    [ProtocolData(ProtocolId.S2S.S2SConnectAck)]
    internal class S2SConnectAck : ProtocolDataBase<S2SConnectAck>
    {
        public S2SConnectResult Result { get; set; } 
    }
}

public enum S2SConnectResult : byte
{
    Success = 0,
    InternalError,
    AlreadyConnected,
}
