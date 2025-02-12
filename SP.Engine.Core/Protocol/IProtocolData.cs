namespace SP.Engine.Core.Protocol
{
    public interface IProtocolData
    {
        EProtocolId ProtocolId { get; }
        bool IsEncrypt { get; }
        uint CompressibleSize { get; }
    }
}
