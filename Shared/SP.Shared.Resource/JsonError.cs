namespace SP.Shared.Resource;

public sealed class JsonErr
{
    public ErrorCode Code { get; set; }
    public string Message { get; set; } = string.Empty;
}

public static class JsonResult
{
    public static JsonRes<T> Ok<T>(int msgId, T payload) => new()
    {
        MsgId = msgId, Ok = true, Payload = payload
    };

    public static JsonRes<object> Error(int msgId, ErrorCode code, string message) => new()
    {
        MsgId = msgId, Ok = false, Payload = null, Error = new JsonErr { Code = code, Message = message }
    };
}
