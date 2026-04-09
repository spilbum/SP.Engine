namespace SP.Engine.Runtime.Networking
{
    public interface IHeader
    {
        ushort ProtocolId { get; }
        bool HasFlag(HeaderFlags flags);
    }
}
