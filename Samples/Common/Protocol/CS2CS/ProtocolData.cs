using SP.Engine.Runtime.Protocol;

namespace Common.Protocol.CS2CS;

[ProtocolData(ProtocolId.CS2CS.ChatNotify)]
public sealed partial class ChatNotify
{
    public string? FromUserId { get; set; }
    public string? TargetUserId { get; set; }
    public string? Message { get; set; }
}
