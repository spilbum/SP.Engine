namespace SP.Engine.Runtime
{
    public enum SessionHandshakeResult
    {
        None = 0,
        Ok,
        InternalError,
        InvalidRequest,
        SessionNotFound,
        ReconnectionNotAllowed,
        KeyExchangeFailed,
    }
}

