namespace SP.Engine.Runtime.Networking
{
    public interface ITcpSender
    {
        bool TrySend(TcpMessage msg);
    }
}
