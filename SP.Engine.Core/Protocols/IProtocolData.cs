namespace SP.Engine.Core.Protocols
{
    public interface IProtocolData
    {
        EProtocolId ProtocolId { get; }
        bool IsEncrypt { get; }
        uint CompressibleSize { get; }
    }
}
