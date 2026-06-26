using SP.Engine.Runtime.Networking;

namespace Common.Protocol.CC2CS;

[ProtocolData(ProtocolId.CC2CS.LoginReq)]
public sealed class LoginReq : ProtocolDataBase<LoginReq>
{
    public string? UserId { get; set; }
}

[ProtocolData(ProtocolId.CC2CS.ChatNotifyReq)]
public sealed class ChatNotifyReq : ProtocolDataBase<ChatNotifyReq>
{
    public string? TargetUserId { get; set; }
    public string? Message { get; set; }
}
