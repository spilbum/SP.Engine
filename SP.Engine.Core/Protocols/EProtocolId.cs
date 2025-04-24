namespace SP.Engine.Core.Protocols
{
    public static class ExtensionMethod
    {
        public static bool IsEngineProtocol(this EProtocolId protocolId)
            => protocolId <= EProtocolId.MaxEngineProtocolId;
    }
    
    public enum EProtocolId : ushort
    {
        None = 0,
        MaxEngineProtocolId = 999,
    }
}
