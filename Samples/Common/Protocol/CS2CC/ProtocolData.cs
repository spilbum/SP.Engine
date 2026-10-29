using SP.Engine.Runtime.Protocol;

namespace Common.Protocol.CS2CC;

[ProtocolData(ProtocolId.CS2CC.LoginAck)]
public sealed partial class LoginAck
{
    
}

[ProtocolData(ProtocolId.CS2CC.ChatNotify)]
public sealed partial class ChatNotify
{
    public string? FromUserId { get; set; }
    public string? Message { get; set; }
}
