using SP.Engine.Runtime.Protocol;

namespace Common.Protocol.CC2CS;

[ProtocolData(ProtocolId.CC2CS.LoginReq)]
public sealed partial class LoginReq
{
    public string? UserId { get; set; }
}

[ProtocolData(ProtocolId.CC2CS.ChatNotifyReq)]
public sealed partial class ChatNotifyReq
{
    public string? TargetUserId { get; set; }
    public string? Message { get; set; }
}
