namespace SP.Engine.Runtime
{
    public enum CloseReason : byte
    {
        Unknown = 0,
        ServerShutdown = 1,
        ClientClosing = 2,
        ServerClosing = 3,
        ApplicationError = 4,
        SocketError = 5,
        TimeOut = 6,
        ProtocolError = 7,
        InternalError = 8,
        LimitExceededResend = 9,
        Rejected = 10,
    }   
}
