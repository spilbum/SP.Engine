
namespace SP.Shared.Resource.Web;

public abstract class JsonResBase
{
    public int MsgId { get; set; }
    public ErrorCode Code { get; set; } = ErrorCode.Unknown;
    public string? Message { get; set; }
}

public sealed class JsonRes<T> : JsonResBase
{
    public T? Payload { get; set; }
}

public static class JsonResult
{
    public static JsonRes<T> Ok<T>(int msgId, T payload)
        => new()
        {
            MsgId = msgId,
            Code = ErrorCode.Success,
            Payload = payload
        };

    public static JsonRes<T> Error<T>(int msgId, ErrorCode code, string message)
        => new()
        {
            MsgId = msgId,
            Code = code,
            Message = message,
            Payload = default
        };
}
