using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Handler
{
    public class ProtocolMethodInvoker
    {
        private readonly Type _type;
        private readonly MethodInfo _method;

        private ProtocolMethodInvoker(EProtocolId protocolId, Type type, MethodInfo method)
        {
            ProtocolId = protocolId;
            _type = type;
            _method = method;
        }

        public EProtocolId ProtocolId { get; }

        public void Invoke(object instance, IMessage message, IEncryptor encryptor)
        {
            var protocol = message.Unpack(_type, encryptor);
            _method.Invoke(instance, new object[] { protocol });
        }
        
        public static IEnumerable<ProtocolMethodInvoker> LoadInvokers(Type type)
        {
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return (from method in methods
                let attr = method.GetCustomAttribute<ProtocolMethodAttribute>()
                where attr != null
                let parameters = method.GetParameters()
                where parameters.Length == 1
                select new ProtocolMethodInvoker(attr.ProtocolId, parameters[0].ParameterType, method)).ToList();
        }
    }
}


