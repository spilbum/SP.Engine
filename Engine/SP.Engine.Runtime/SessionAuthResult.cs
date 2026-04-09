namespace SP.Engine.Runtime
{
    public enum SessionAuthResult
    {
        None = 0,
        Ok,
        InternalError,
        InvalidRequest,
        SessionNotFound,
        ReconnectionNotAllowed,
        KeyExchangeFailed
    }
}
