namespace SP.Engine.SourceGenerator
{
    internal static class KnownSymbols
    {
        public const string GlobalNamespace = "SP.Engine.Generated";
        
        // Metadata Attribute Names
        public const string ProtocolDataAttribute = "SP.Engine.Runtime.Protocol.ProtocolDataAttribute";
        public const string ProtocolHandlerAttribute = "SP.Engine.Runtime.Protocol.ProtocolHandlerAttribute";

        // Base Types & Interfaces
        public const string ProtocolHandlerBase = "ProtocolHandlerBase";
        public const string ProtocolHandlerBaseNamespace = "SP.Engine.Runtime.Protocol";

        // Collection Generic Definitions
        public const string ListGenericDef = "System.Collections.Generic.List<T>";
        public const string DictionaryGenericDef = "System.Collections.Generic.Dictionary<TKey, TValue>";
        public const string DateTimeType = "System.DateTime";

        // Fully Qualified Code Generation Type References
        public const string GlobalNetWriter = "global::SP.Core.Serialization.NetWriter";
        public const string GlobalNetReader = "global::SP.Core.Serialization.NetReader";
        public const string GlobalChannelKind = "global::SP.Engine.Runtime.Channel.ChannelKind";
        public const string GlobalProtocolPolicyToggle = "global::SP.Engine.Runtime.Protocol.Toggle";
        public const string GlobalProtocolPolicyOverrides = "global::SP.Engine.Runtime.Protocol.ProtocolPolicyOverrides";
        public const string GlobalIProtocolPolicyRegistry = "global::SP.Engine.Runtime.Protocol.IProtocolPolicyRegistry";
        public const string GlobalProtocolPolicyRegistryAttr = "global::SP.Engine.Runtime.Protocol.ProtocolPolicyRegistryAttribute";
        public const string GlobalIProtocolHandlerRegistry = "global::SP.Engine.Runtime.Protocol.IProtocolHandlerRegistry";
        public const string GlobalProtocolHandlerRegistryAttr = "global::SP.Engine.Runtime.Protocol.ProtocolHandlerRegistryAttribute";
        public const string GlobalIProtocolHandler = "global::SP.Engine.Runtime.Protocol.IProtocolHandler";
        public const string GlobalIProtocolData = "global::SP.Engine.Runtime.Protocol.IProtocolData";
    }
}
