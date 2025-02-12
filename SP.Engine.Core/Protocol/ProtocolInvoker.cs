using System;
using System.Linq;
using System.Reflection;
using SP.Engine.Core.Message;

namespace SP.Engine.Core.Protocol
{
    public class ProtocolInvoker
    {
        public EProtocolId ProtocolId { get; private set; }

        private readonly MethodInfo _method;
        private readonly Type _protocolType;

        public ProtocolInvoker(EProtocolId protocolId, MethodInfo method)
        {
            if (null == method || null == method.DeclaringType)
                throw new ArgumentException("Invalid methodInfo");

            var parameters = method.GetParameters();
            if (0 == parameters.Length)
                throw new ArgumentException($"No method parameter. protocolId={protocolId}, methodName={method.Name}");

            ProtocolId = protocolId;
            _method = method;            
            _protocolType = parameters[0].ParameterType;
        }

        public void Invoke(object instance, IMessage message, byte[] sharedKey)
        {
            if (null == instance || null == message)
                return;

            try
            {
                // 메시지 역직렬화
                var protocol = message.DeserializeProtocol(_protocolType, sharedKey);
                if (null == protocol)
                    throw new InvalidOperationException($"Failed to deserialize the message: protocolId={message.ProtocolId}");

                var instanceType = instance.GetType();
                if (instanceType.IsGenericType)
                {
                    // 제너릭 타입은 인스턴스 타입(닫힌 타입)을 통해 메소드를 가져옵니다.
                    var method = instanceType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                        .SingleOrDefault(x => x.Name.Equals(_method.Name));
                    
                    if (null == method)
                        throw new InvalidOperationException($"Method not found. name={_method.Name}");

                    method.Invoke(instance, new object[] { protocol });
                }
                else
                {
                    _method.Invoke(instance, new object[] { protocol });
                }
            }
            catch (TargetInvocationException ex)
            {
                var e = ex.InnerException;
                if (null != e)
                    throw e;
            }
        }
    }
}
