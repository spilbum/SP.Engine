namespace SP.Engine.Runtime.Networking
{
    public interface IUnreliableSender
    {
        bool TrySend(UdpMessage msg);
    }
}
