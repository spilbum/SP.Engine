using System;
using SP.Engine.Runtime;

namespace SP.Engine.Server;

public class SessionAuthException(SessionHandshakeResult result, string message) : Exception(message)
{
    public SessionHandshakeResult Result { get; } = result;
}
