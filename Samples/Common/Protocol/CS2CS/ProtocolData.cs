using SP.Engine.Runtime.Networking;

namespace Common.Protocol.CS2CS;

[ProtocolData(ProtocolId.CS2CS.ChatNotify)]
public sealed class ChatNotify : ProtocolDataBase<ChatNotify>
{
    public string? FromUserId { get; set; }
    public string? TargetUserId { get; set; }
    public string? Message { get; set; }
}
