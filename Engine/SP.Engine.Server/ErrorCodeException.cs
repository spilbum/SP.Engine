using System;
using SP.Engine.Runtime;

namespace SP.Engine.Server;

public class ErrorCodeException(EngineErrorCode errorCode, string message) : Exception(message)
{
    public EngineErrorCode ErrorCode { get; } = errorCode;
}
