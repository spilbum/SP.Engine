using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Common.Protocol.S2S
{
    [ProtocolData(ProtocolId.S2S.S2SConnectReq)]
    internal partial class S2SConnectReq
    {
        public string? Category { get; set; }
    }

    [ProtocolData(ProtocolId.S2S.S2SConnectAck)]
    internal partial class S2SConnectAck
    {
        public S2SConnectResult Result { get; set; } 
    }

    public enum S2SConnectResult : byte
    {
        Success = 0,
        InternalError,
        AlreadyConnected,
    }
}
