using System;
using System.Collections.Generic;
using System.Reflection;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Protocol
{
    public class ProtocolMethodInvoker
    {
        private readonly MethodInfo _method;
        private readonly Type _paramType;

        public ushort Id { get; }
        
        private ProtocolMethodInvoker(ushort id, MethodInfo method, Type paramType)
        {
            Id = id;
            _method = method;
            _paramType = paramType;
        }

        public void Invoke(object instance, IMessage message, IEncryptor encryptor, ICompressor compressor)
        {
            var p = message.Deserialize(_paramType, encryptor, compressor);
            _method.Invoke(instance, new object[] { p });
        }

        public static IEnumerable<ProtocolMethodInvoker> Load(Type type)
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                   BindingFlags.NonPublic))
            {
                var attr = method.GetCustomAttribute<ProtocolMethodAttribute>();
                if (attr == null)
                    continue;

                var args = method.GetGenericArguments();
                if (args.Length == 1 && typeof(IProtocol).IsAssignableFrom(args[0]))
                    yield return new ProtocolMethodInvoker(attr.Id, method, args[0]);
            }
        }
    }
}


