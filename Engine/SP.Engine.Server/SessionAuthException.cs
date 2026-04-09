using System;
using SP.Engine.Runtime;

namespace SP.Engine.Server;

public class SessionAuthException(SessionAuthResult result, string message) : Exception(message)
{
    public SessionAuthResult Result { get; } = result;
}
