using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace SP.Common.Accessor
{
    public class PropertyTypeAccessor
    {
        private readonly Func<object, object> _getter;
        private readonly Action<object, object> _setter;
        
        public string Name { get; }
        public Type Type { get; }
        
        public bool CanRead { get; }
        public bool CanWrite { get; }
        
        public PropertyTypeAccessor(PropertyInfo propertyInfo, IgnorePropertyAttribute ignoreAttr = null)
        {
            Name = propertyInfo.Name;
            Type = propertyInfo.PropertyType;

            CanRead = !(ignoreAttr?.IgnoreOnRead ?? false);
            CanWrite = !(ignoreAttr?.IgnoreOnWrite ?? false);
            
            if (CanRead)
                _getter = CreateGetter(propertyInfo);
            if (CanWrite)
                _setter = CreateSetter(propertyInfo);
        }
        
        public bool IsNullable()
        {
            return !Type.IsValueType || Nullable.GetUnderlyingType(Type) != null;
        }

        public object GetValue(object instance)
        {
            if (!CanRead || _getter == null)
                throw new InvalidOperationException($"Property '{Name}' is not readable.");
            return _getter(instance);
        }

        public void SetValue(object instance, object value)
        {
            if (!CanWrite || _setter == null)
                throw new InvalidOperationException($"Property '{Name}' is not writable.");
            _setter(instance, value);
        }
        
        private static Func<object, object> CreateGetter(PropertyInfo propertyInfo)
        {
            if (!propertyInfo.CanRead)
                throw new ArgumentException($"Property {propertyInfo.Name} does not have a getter.");
            
            if (propertyInfo.DeclaringType == null)
                throw new ArgumentException($"Property {propertyInfo.Name} declaring type is null.");
            
            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var instanceCvt = Expression.Convert(instanceParam, propertyInfo.DeclaringType);
            var property = Expression.Property(instanceCvt, propertyInfo);
            var returnCvt = Expression.Convert(property, typeof(object));

            var lambda = Expression.Lambda<Func<object, object>>(returnCvt, instanceParam);
            return lambda.Compile();
        }

        private static Action<object, object> CreateSetter(PropertyInfo propertyInfo)
        {
            if (!propertyInfo.CanWrite)
                throw new ArgumentException($"Property {propertyInfo.Name} does not have a setter.");
            
            if (propertyInfo.DeclaringType == null)
                throw new ArgumentException($"Property {propertyInfo.Name} declaring type is null.");
            
            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var valueParam = Expression.Parameter(typeof(object), "value");
            
            var instanceCvt = Expression.Convert(instanceParam, propertyInfo.DeclaringType);
            var valueCvt = Expression.Convert(valueParam, propertyInfo.PropertyType);
            
            var property = Expression.Property(instanceCvt, propertyInfo);
            var assign = Expression.Assign(property, valueCvt);
            
            var lambda = Expression.Lambda<Action<object, object>>(assign, instanceParam, valueParam);
            return lambda.Compile();
        }
    }
}
