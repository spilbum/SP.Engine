using System;
using System.Linq.Expressions;
using System.Reflection;

namespace SP.Common.Accessor
{
    public class PropertyAccessor : IMemberAccessor
    {
        private readonly Func<object, object> _getter;
        private readonly Action<object, object> _setter;
        
        public string Name { get; }
        public Type Type { get; }
        public bool CanRead { get; }
        public bool CanWrite { get; }
        
        public PropertyAccessor(PropertyInfo propertyInfo)
        {
            var attr = propertyInfo.GetCustomAttribute<MemberAttribute>();
            Name = attr == null ? propertyInfo.Name : attr.Name;
            Type = propertyInfo.PropertyType;

            var ignoreAttr = propertyInfo.GetCustomAttribute<IgnoreMemberAttribute>();
            CanRead = !(ignoreAttr?.IgnoreOnRead ?? false);
            CanWrite = !(ignoreAttr?.IgnoreOnWrite ?? false);
            
            if (CanRead) _getter = CreateGetter(propertyInfo);
            if (CanWrite) _setter = CreateSetter(propertyInfo);
        }
        
        public object GetValue(object instance) => _getter(instance);
        public void SetValue(object instance, object value) => _setter(instance, value);
        
        private static Func<object, object> CreateGetter(PropertyInfo property)
        {
            if (property.DeclaringType == null)
                throw new ArgumentException("Property declaring type is null.", nameof(property));
            
            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var instanceCast = Expression.Convert(instanceParam, property.DeclaringType);
            var propertyAccess = Expression.Property(instanceCast, property);
            var returnCast = Expression.Convert(propertyAccess, typeof(object));
            return Expression.Lambda<Func<object, object>>(returnCast, instanceParam).Compile();
        }

        private static Action<object, object> CreateSetter(PropertyInfo property)
        {
            if (property.DeclaringType == null)
                throw new ArgumentException("Property declaring type is null.", nameof(property));
            
            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var valueParam = Expression.Parameter(typeof(object), "value");
            var instanceCast = Expression.Convert(instanceParam, property.DeclaringType);
            var valueCast = Expression.Convert(valueParam, property.PropertyType);
            var propertyCast = Expression.Property(instanceCast, property);
            var assign = Expression.Assign(propertyCast, valueCast);
            return Expression.Lambda<Action<object, object>>(assign, instanceParam, valueParam).Compile();
        }
    }
}
