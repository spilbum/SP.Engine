namespace SP.Engine.Runtime.Protocol
{
    public interface IProtocolData
    {
        EProtocolId ProtocolId { get; }
        bool IsEncrypt { get; }
        uint CompressibleSize { get; }
    }
}
