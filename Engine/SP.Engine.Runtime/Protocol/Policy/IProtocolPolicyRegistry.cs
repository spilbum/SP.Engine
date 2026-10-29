using System;
using System.Collections.Generic;

namespace SP.Engine.Runtime.Protocol
{
    public interface IProtocolPolicyRegistry
    {
        void Register(IDictionary<ushort, ProtocolPolicyOverrides> overrides);
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class ProtocolPolicyRegistryAttribute : Attribute
    {
        public Type RegistryType { get; }
        public ProtocolPolicyRegistryAttribute(Type registryType) => RegistryType = registryType;
    }
}
