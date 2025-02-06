using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SP.Engine.Common.Accessor;

namespace SP.Engine.Core.Protocol
{
    public static class ProtocolManager
    {
        private static readonly Dictionary<EProtocolId, string> ProtocolNameDict = new Dictionary<EProtocolId, string>();
        private static readonly Dictionary<EProtocolId, ProtocolInvoker> ProtocolInvokerDict = new Dictionary<EProtocolId, ProtocolInvoker>();
        private static readonly Dictionary<EProtocolId, RuntimeTypeAccessor> ProtocolDataDict = new Dictionary<EProtocolId, RuntimeTypeAccessor>();
        private static Action<Exception> _exceptionHandler;
        
        public static IEnumerable<string> ProtocolNameList
            => ProtocolNameDict.Values;

        public static bool Initialize(List<Assembly> assemblies, Action<Exception> exceptionHandler = null)
        {
            assemblies.Add(Assembly.GetExecutingAssembly());
            
            _exceptionHandler = exceptionHandler;
            if (assemblies.Any(assembly => !SetupProtocolDefiner(assembly)
                                           || !SetupProtocolData(assembly)
                                           || !SetupProtocolHandler(assembly)))
            {
                return false;
            }
            
            // 정의되지 않은 프로토콜 데이터가 있는지 확인
            foreach (var protocolId in ProtocolDataDict.Keys.Where(p => !ProtocolNameDict.ContainsKey(p)))
            {
                OnError(new Exception($"The protocol data for the ID {protocolId} is not defined."));
                return false;
            }
            
            // 정의되지 않은 프로토콜 핸드러가 있는지 체크
            foreach (var protocolId in ProtocolInvokerDict.Keys.Where(p => !ProtocolNameDict.ContainsKey(p)))
            {
                OnError(new Exception($"The protocol handler for the ID {protocolId} is not defined."));
                return false;
            }
            
            return true;
        }
        
        public static string GetProtocolName(EProtocolId protocolId)
        {
            ProtocolNameDict.TryGetValue(protocolId, out var name);
            return name;
        }

        public static ProtocolInvoker GetProtocolInvoker(EProtocolId protocolId)
        {
            ProtocolInvokerDict.TryGetValue(protocolId, out var handler);
            return handler;
        }

        private static void OnError(Exception e)
        {
            _exceptionHandler?.Invoke(e);
        }

        private static bool SetupProtocolDefiner(Assembly assembly)
        {
            var types = GetTypesByInterface<IProtocolDefiner>(assembly);
            foreach (var type in types)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
                foreach (var field in fields)
                {
                    var protocolId = (EProtocolId)field.GetRawConstantValue();
                    var name = null == field.DeclaringType
                        ? field.Name
                        : $"{field.DeclaringType.Name}.{field.Name}";

                    if (ProtocolNameDict.TryAdd(protocolId, name)) continue;
                    OnError(new Exception(
                        $"The protocol ID has been declared redundantly. protocol=[{protocolId}, {name}]"));
                    return false;
                }
            }

            return true;
        }

        private static Type[] GetTypesByInterface<T>(Assembly assembly)
        {
            var types = assembly.GetTypes();
            return types
                .Where(t => typeof(T).IsAssignableFrom(t) && t.IsAbstract == false && !t.IsInterface)
                .ToArray();
        }

        private static bool SetupProtocolData(Assembly assembly)
        {
            try
            {
                var types = GetTypesByInterface<IProtocolData>(assembly);
                foreach (var type in types)
                {
                    var attribute = type.GetCustomAttribute<ProtocolAttribute>();
                    if (null == attribute)
                        throw new Exception($"Invalid protocol type: {type}");

                    var runtimeTypeAccessor = RuntimeTypeAccessor.GetOrCreate(type);
                    ProtocolDataDict.Add(attribute.ProtocolId, runtimeTypeAccessor);
                }
                return true;
            }
            catch (Exception e)
            {
                OnError(e);
                return false;
            }
        }

        private static bool SetupProtocolHandler(Assembly assembly)
        {
            var types = GetTypesByInterface<IProtocolHandler>(assembly);
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var method in methods)
                {
                    var attribute = method.GetCustomAttribute<ProtocolHandlerAttribute>();
                    if (null == attribute)
                        continue;

                    var protocolId = attribute.ProtocolId;
                    if (ProtocolInvokerDict.ContainsKey(protocolId))
                    {
                        OnError(new Exception(
                            $"The protocol handler has been declared redundantly. protocolId={protocolId}, methodName={method.Name}"));
                        return false;
                    }

                    ProtocolInvokerDict[protocolId] = new ProtocolInvoker(protocolId, method);
                }
            }

            return true;

        }
    }
}
