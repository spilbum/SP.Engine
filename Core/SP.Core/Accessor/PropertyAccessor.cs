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
            Info = p;

            if (CanGet)
                _getter = CreateGetter(p);
            if (CanSet)
                _setter = CreateSetter(p);
        }

        public string Name { get; }
        public Type Type { get; }
        public int Order { get; }
        public bool IgnoreGet { get; }
        public bool IgnoreSet { get; }
        public bool CanGet { get; }
        public bool CanSet { get; }
        public MemberInfo Info { get; }
        
        public object GetValue(object instance) => _getter(instance);
        public void SetValue(object instance, object value) => _setter(instance, value);

        private static Action<object, object> CreateSetter(PropertyInfo p)
        {
            var objParam = Expression.Parameter(typeof(object), "obj");
            var valueParam = Expression.Parameter(typeof(object), "value");
            var target = Expression.Property(Expression.Convert(objParam, p.DeclaringType!), p);
            var type = p.PropertyType;
            var assign = Expression.Assign(target, Expression.Convert(valueParam, type));
            return Expression.Lambda<Action<object, object>>(assign, objParam, valueParam).Compile();
        }

        private static Func<object, object> CreateGetter(PropertyInfo p)
        {
            var objParam = Expression.Parameter(typeof(object), "obj");
            var body = Expression.Property(Expression.Convert(objParam, p.DeclaringType!), p);
            var convert = Expression.Convert(body, typeof(object));
            return Expression.Lambda<Func<object, object>>(convert, objParam).Compile();
        }
    }
}
