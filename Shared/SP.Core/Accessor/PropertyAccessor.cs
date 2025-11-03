using System;
using System.Linq.Expressions;
using System.Reflection;

namespace SP.Core.Accessor
{
    public class PropertyAccessor : IMemberAccessor
    {
        private readonly Func<object, object> _getter;
        private readonly Action<object, object> _setter;

        public PropertyAccessor(PropertyInfo p)
        {
            var attr = p.GetCustomAttribute<MemberAttribute>();

            Name = attr?.Name ?? p.Name;
            Type = p.PropertyType;
            Order = attr?.Order ?? int.MaxValue;
            IgnoreGet = attr?.IgnoreGet ?? false;
            IgnoreSet = attr?.IgnoreSet ?? false;
            CanGet = p.GetMethod != null && p.GetMethod.IsPublic && !p.GetMethod.IsStatic;
            CanSet = p.SetMethod != null && p.SetMethod.IsPublic && !p.SetMethod.IsStatic;
            if (CanGet)
                _getter = BuildGetter(p);
            if (CanSet)
                _setter = BuildSetter(p);
        }

        public string Name { get; }
        public Type Type { get; }
        public int Order { get; }
        public bool IgnoreGet { get; }
        public bool IgnoreSet { get; }
        public bool CanGet { get; }
        public bool CanSet { get; }

        public object GetValue(object instance)
        {
            if (!CanGet || IgnoreGet || _getter == null)
                throw new InvalidOperationException($"Getter not available for '{Name}'");
            return _getter(instance);
        }

        public void SetValue(object instance, object value)
        {
            if (!CanSet || IgnoreSet || _setter == null)
                throw new InvalidOperationException($"Setter not available for '{Name}'");
            _setter(instance, value);
        }

        private static Func<object, object> BuildGetter(PropertyInfo p)
        {
            var obj = Expression.Parameter(typeof(object), "obj");
            var cast = Expression.Convert(obj, p.DeclaringType!);
            var box = Expression.Convert(Expression.Property(cast, p), typeof(object));
            return Expression.Lambda<Func<object, object>>(box, obj).Compile();
        }

        private static Action<object, object> BuildSetter(PropertyInfo p)
        {
            var obj = Expression.Parameter(typeof(object), "obj");
            var value = Expression.Parameter(typeof(object), "value");
            var castObj = Expression.Convert(obj, p.DeclaringType!);
            var castValue = Expression.Convert(value, p.PropertyType);
            var assign = Expression.Assign(Expression.Property(castObj, p), castValue);
            return Expression.Lambda<Action<object, object>>(assign, obj, value).Compile();
        }
    }
}
