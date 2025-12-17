using SP.Shared.Resource;

namespace ResourceServer;

public class ErrorCodeException(ErrorCode code, string? message = null) : Exception(message)
{
    public ErrorCode Code { get; } = code;
}
