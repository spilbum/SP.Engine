namespace SP.Engine.Runtime
{
    public enum SessionAuthResult
    {
        None = 0,
        Ok,
        InternalError,
        InvalidRequest,
        PeerNotFound,
        ReconnectionNotAllowed,
        KeyExchangeFailed
    }
}
