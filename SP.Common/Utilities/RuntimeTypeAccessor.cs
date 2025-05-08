using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace SP.Common.Utilities
{
    public class TypeProperty
    {
        private readonly Func<object, object> _getter;
        private readonly Action<object, object> _setter;
        
        public string Name { get; }
        public Type Type { get; }
        
        public TypeProperty(PropertyInfo propertyInfo)
        {
            if (null == propertyInfo)
                throw new ArgumentNullException(nameof(propertyInfo));
            
            Name = propertyInfo.Name;
            Type = propertyInfo.PropertyType;
            _getter = PropertyAccessor.CreateGetter(propertyInfo);
            _setter = PropertyAccessor.CreateSetter(propertyInfo);
        }
        
        public bool IsNullable()
        {
            return !Type.IsValueType || Nullable.GetUnderlyingType(Type) != null;
        }

        public object GetValue(object instance)
            => _getter(instance);

        public T GetValue<T>(object instance)
        {
            var value = _getter(instance);
            if (null == value)
                return default;
            
            if (value is T result)
                return result;

            return (T)Convert.ChangeType(value, typeof(T));
        }

        public void SetValue(object instance, object value)
            => _setter(instance, value);

        public void SetValue<T>(object instance, T value)
        {
            var convertedValue = value;
            if (EqualityComparer<T>.Default.Equals(value, default))
            {
                if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null)
                    throw new InvalidCastException($"Cannot cast null to non-nullable type {typeof(T)}");
                convertedValue = default;
            }
            
            _setter(instance, convertedValue);
        }
    }

    public class RuntimeTypeAccessor
    {
        private static readonly ConcurrentDictionary<Type, Lazy<RuntimeTypeAccessor>> RuntimeTypeAccessorDict =
            new ConcurrentDictionary<Type, Lazy<RuntimeTypeAccessor>>();

        private readonly Dictionary<string, TypeProperty> _propertyDict;

        public string Name { get; }
        public Type Type { get; }
        public IEnumerable<TypeProperty> Properties => _propertyDict.Values;

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

        private RuntimeTypeAccessor(string name, Type type, Dictionary<string, TypeProperty> propertyDict)
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

            var propertyDict = new Dictionary<string, TypeProperty>();
            foreach (var property in properties)
            {
                if (null != property.GetCustomAttribute<IgnorePropertyAttribute>())
                    continue;
                
                if (!propertyDict.ContainsKey(property.Name))
                    propertyDict[property.Name] = new TypeProperty(property);
            }

            return new RuntimeTypeAccessor(name, type, propertyDict);
        }
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class IgnorePropertyAttribute : Attribute
{
    
}
