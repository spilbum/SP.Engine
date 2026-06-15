namespace SP.Engine.Runtime
{
    public enum SessionAuthResult
    {
        Ok,
        InternalError,
        InvalidRequest,
        PeerNotFound,
        ReconnectionNotAllowed,
        KeyExchangeFailed
    }
}
