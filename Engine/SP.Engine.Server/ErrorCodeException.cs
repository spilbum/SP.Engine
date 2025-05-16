using System;
using SP.Engine.Runtime;

namespace SP.Engine.Server;

public class ErrorCodeException(EEngineErrorCode errorCode, string message) : Exception(message)
{
    public EEngineErrorCode ErrorCode { get; } = errorCode;
}
