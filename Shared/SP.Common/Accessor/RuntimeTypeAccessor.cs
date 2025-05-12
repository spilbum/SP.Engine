using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SP.Common.Accessor
{
    public class RuntimeTypeAccessor
    {
        private static readonly ConcurrentDictionary<Type, Lazy<RuntimeTypeAccessor>> RuntimeTypeAccessorDict =
            new ConcurrentDictionary<Type, Lazy<RuntimeTypeAccessor>>();

        private readonly Dictionary<string, PropertyAccessor> _propertyDict;

        public string Name { get; }
        public Type Type { get; }
        public List<PropertyAccessor> Properties => _propertyDict.Values.ToList();

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

        private RuntimeTypeAccessor(string name, Type type, Dictionary<string, PropertyAccessor> propertyDict)
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

            var propertyDict = new Dictionary<string, PropertyAccessor>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in properties)
            {
                var ignoreAttr = property.GetCustomAttribute<IgnorePropertyAttribute>();
                if (ignoreAttr != null && ignoreAttr.IgnoreOnRead && ignoreAttr.IgnoreOnWrite)
                    continue;
                
                var accessor = new PropertyAccessor(property);
                if (!propertyDict.TryAdd(accessor.Name, accessor))
                    throw new InvalidOperationException($"Property '{accessor.Name}' is already added.");
            }

            return new RuntimeTypeAccessor(name, type, propertyDict);
        }
    }
}


