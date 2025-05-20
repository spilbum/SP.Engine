using System;
using System.Linq.Expressions;
using System.Reflection;

namespace SP.Common.Accessor
{
    public class FieldAccessor : IMemberAccessor
    {
        private readonly Func<object, object> _getter;
        private readonly Action<object, object> _setter;

        public string Name { get; }
        public Type Type { get; }
        public bool CanRead { get; }
        public bool CanWrite { get; }

        public FieldAccessor(FieldInfo fieldInfo)
        {
            var attr = fieldInfo.GetCustomAttribute<MemberAttribute>();
            Name = attr == null ? fieldInfo.Name : attr.Name;
            Type = fieldInfo.FieldType;

            var ignoreAttr = fieldInfo.GetCustomAttribute<IgnoreMemberAttribute>();
            CanRead = !(ignoreAttr?.IgnoreOnRead ?? false);
            CanWrite = !(ignoreAttr?.IgnoreOnWrite ?? false);

            if (CanRead) _getter = CreateGetter(fieldInfo);
            if (CanWrite) _setter = CreateSetter(fieldInfo);
        }

        public object GetValue(object instance) => _getter(instance);
        public void SetValue(object instance, object value) => _setter(instance, value);

        private static Func<object, object> CreateGetter(FieldInfo field)
        {
            if (field.DeclaringType == null)
                throw new ArgumentException("Field declaring type is null", nameof(field));
            
            var instance = Expression.Parameter(typeof(object), "instance");
            var instanceCast = Expression.Convert(instance, field.DeclaringType);
            var fieldAccess = Expression.Field(instanceCast, field);
            var castToObject = Expression.Convert(fieldAccess, typeof(object));
            return Expression.Lambda<Func<object, object>>(castToObject, instance).Compile();
        }

        private static Action<object, object> CreateSetter(FieldInfo field)
        {
            if (field.DeclaringType == null)
                throw new ArgumentException("Field declaring type is null", nameof(field));
            
            var instance = Expression.Parameter(typeof(object), "instance");
            var value = Expression.Parameter(typeof(object), "value");
            var instanceCast = Expression.Convert(instance, field.DeclaringType);
            var valueCast = Expression.Convert(value, field.FieldType);
            var fieldAccess = Expression.Field(instanceCast, field);
            var assign = Expression.Assign(fieldAccess, valueCast);
            return Expression.Lambda<Action<object, object>>(assign, instance, value).Compile();
        }
    }
}
