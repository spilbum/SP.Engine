
namespace SP.Shared.Resource;

public abstract class JsonResBase
{
    public int MsgId { get; set; }
    public bool Ok { get; set; } = true;
    public JsonErr? Error { get; set; }
}

public sealed class JsonRes<TPayload> : JsonResBase
{
    public TPayload? Payload { get; set; }
}
