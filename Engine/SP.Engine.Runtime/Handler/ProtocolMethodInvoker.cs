using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SP.Engine.Runtime.Message;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Handler
{
    public class ProtocolMethodInvoker
    {
        private readonly Type _protocolType;
        private readonly MethodInfo _methodInfo;

        private ProtocolMethodInvoker(EProtocolId protocolId, Type protocolType, MethodInfo methodInfo)
        {
            _protocolType = protocolType;
            _methodInfo = methodInfo;
            ProtocolId = protocolId;
        }

        public EProtocolId ProtocolId { get; }

        public void Invoke(object instance, IMessage message, byte[] sharedKey)
        {
            var protocol = message.DeserializeProtocol(_protocolType, sharedKey);
            _methodInfo.Invoke(instance, new object[] { protocol });
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


