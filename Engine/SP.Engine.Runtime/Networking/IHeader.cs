namespace SP.Engine.Runtime.Networking
{
    public interface IHeader
    {
        ushort MsdId { get; }
        bool HasFlag(HeaderFlags flags);
    }
}
