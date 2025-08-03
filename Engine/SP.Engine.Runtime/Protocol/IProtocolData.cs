namespace SP.Engine.Runtime.Protocol
{
    public interface IProtocolData
    {
        EProtocolId ProtocolId { get; }
        EProtocolType ProtocolType { get; }
    }
}
