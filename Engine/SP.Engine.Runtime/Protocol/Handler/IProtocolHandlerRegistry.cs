using System;
using System.Collections.Generic;

namespace SP.Engine.Runtime.Protocol
{
    public interface IProtocolHandlerRegistry
    {
        void Register(IDictionary<ushort, IProtocolHandler> handlers, Func<Type, bool> contextFilter);
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class ProtocolHandlerRegistryAttribute : Attribute
    {
        public Type RegistryType { get; }
        public ProtocolHandlerRegistryAttribute(Type registryType) => RegistryType = registryType;
    }
}
