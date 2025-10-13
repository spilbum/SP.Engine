namespace SP.Engine.Runtime.Networking
{
    public interface IReliableSender
    {
        bool TrySend(TcpMessage message);
    }
}
