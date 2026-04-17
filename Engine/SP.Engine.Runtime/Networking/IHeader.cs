namespace SP.Engine.Runtime.Networking
{
    public interface IHeader
    {
        ushort Id { get; }
        int BodyLength { get; }
        bool HasFlag(HeaderFlags flags);
    }
}
