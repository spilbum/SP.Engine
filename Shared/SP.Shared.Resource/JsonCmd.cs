
namespace SP.Shared.Resource;

public abstract class JsonCmdBase
{
    public int MsgId { get; set; }
}

public sealed class JsonCmd<TPayload> : JsonCmdBase
{
    public TPayload? Payload { get; set; }

    public JsonCmd()
    {
    }

    public JsonCmd(int msgId, TPayload payload)
    {
        MsgId = msgId;
        Payload = payload;
    }
}
