using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace SP.Common.Accessor
{
    public class RuntimeTypeAccessor
    {
        private static readonly ConcurrentDictionary<Type, Lazy<RuntimeTypeAccessor>> RuntimeTypeAccessorDict =
            new ConcurrentDictionary<Type, Lazy<RuntimeTypeAccessor>>();

        private readonly Dictionary<string, PropertyTypeAccessor> _propertyDict;

        public string Name { get; }
        public Type Type { get; }
        public IEnumerable<PropertyTypeAccessor> Properties => _propertyDict.Values;

        public object this[object instance, string name]
        {
            get
            {
                if (instance == null)
                    return null;

                if (!_propertyDict.TryGetValue(name, out var property))
                    throw new InvalidOperationException($"Property '{name}' not found in type '{Name}'.");
                return property.GetValue(instance);
            }
            set
            {
                if (instance == null)
                    return;

                if (!_propertyDict.TryGetValue(name, out var property))
                    throw new InvalidOperationException($"Property '{name}' not found in type '{Name}'.");
                property.SetValue(instance, value);
            }
        }

        private RuntimeTypeAccessor(string name, Type type, Dictionary<string, PropertyTypeAccessor> propertyDict)
        {
            Name = name;
            Type = type;
            _propertyDict = propertyDict;
        }

        public static RuntimeTypeAccessor GetOrCreate(Type type)
        {
            if (!type.IsClass)
                throw new ArgumentException($"Type {type.FullName} is not a class");

            return RuntimeTypeAccessorDict.GetOrAdd(type,
                key => new Lazy<RuntimeTypeAccessor>(() => CreateTypeDefined(key))).Value;
        }

        private static RuntimeTypeAccessor CreateTypeDefined(Type type)
        {
            var name = type.Name;
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);

            var propertyDict = new Dictionary<string, PropertyTypeAccessor>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in properties)
            {
                var ignoreAttr = property.GetCustomAttribute<IgnorePropertyAttribute>();
                if (ignoreAttr != null && ignoreAttr.IgnoreOnRead && ignoreAttr.IgnoreOnWrite)
                    continue;
                
                if (!propertyDict.ContainsKey(property.Name))
                    propertyDict[property.Name] = new PropertyTypeAccessor(property, ignoreAttr);
            }

            return new RuntimeTypeAccessor(name, type, propertyDict);
        }
    }
}


