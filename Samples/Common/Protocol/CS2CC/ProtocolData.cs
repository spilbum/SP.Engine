using SP.Engine.Runtime.Networking;

namespace Common.Protocol.CS2CC;

[ProtocolData(ProtocolId.CS2CC.LoginAck)]
public sealed class LoginAck : ProtocolDataBase<LoginAck>
{
    
}

[ProtocolData(ProtocolId.CS2CC.ChatNotify)]
public sealed class ChatNotify : ProtocolDataBase<ChatNotify>
{
    public string? FromUserId { get; set; }
    public string? Message { get; set; }
}
