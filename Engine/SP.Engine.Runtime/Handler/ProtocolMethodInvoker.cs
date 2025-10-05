using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Handler
{
    public class ProtocolMethodInvoker
    {
        private readonly Type _type;
        private readonly MethodInfo _method;

        private ProtocolMethodInvoker(ushort id, Type type, MethodInfo method)
        {
            Id = id;
            _type = type;
            _method = method;
        }

        public ushort Id { get; }

        public void Invoke(object instance, IMessage message, IEncryptor encryptor, ICompressor compressor)
        {
            var protocol = message.Deserialize(_type, encryptor, compressor);
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
                select new ProtocolMethodInvoker(attr.Id, parameters[0].ParameterType, method)).ToList();
        }
    }
}


