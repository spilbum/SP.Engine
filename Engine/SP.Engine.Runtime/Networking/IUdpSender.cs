namespace SP.Engine.Runtime.Networking
{
    public interface IUdpSender
    {
        bool TrySend(UdpMessage msg);
    }
}
