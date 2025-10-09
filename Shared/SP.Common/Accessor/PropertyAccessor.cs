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
        public int Order { get; }
        
        public PropertyAccessor(PropertyInfo p)
        {
            Name = GetName(p);
            Type = p.PropertyType;
            Order = GetOrder(p);
            _getter = BuildGetter(p);
            _setter = BuildSetter(p);
        }

        private static string GetName(PropertyInfo p)
        {
            var attr = p.GetCustomAttribute<MemberNameAttribute>();
            return attr?.Name ?? p.Name;
        }

        private static int GetOrder(PropertyInfo f)
        {
            var attr = f.GetCustomAttribute<MemberOrderAttribute>();
            return attr?.Order ?? int.MaxValue;
        }

        public object GetValue(object instance)
        {
            if (_getter == null)
                throw new InvalidOperationException("Getter not set");
            return _getter(instance);
        }

        public void SetValue(object instance, object value)
        {
            if (_setter == null)
                throw new InvalidOperationException("Setter not set");
            _setter(instance, value);
        }
        
        private static Func<object, object> BuildGetter(PropertyInfo p)
        {
            if (p.DeclaringType == null)
                throw new ArgumentException("Property declaring type is null.", nameof(p));
            
            var obj = Expression.Parameter(typeof(object), "obj");
            var cast = Expression.Convert(obj, p.DeclaringType);
            var prop = Expression.Property(cast, p);
            var box = Expression.Convert(prop, typeof(object));
            return Expression.Lambda<Func<object, object>>(box, obj).Compile();
        }

        private static Action<object, object> BuildSetter(PropertyInfo p)
        {
            if (p.DeclaringType == null)
                throw new ArgumentException("Property declaring type is null.", nameof(p));
            
            var obj = Expression.Parameter(typeof(object), "obj");
            var value = Expression.Parameter(typeof(object), "value");
            var castObj = Expression.Convert(obj, p.DeclaringType);
            var castValue = Expression.Convert(value, p.PropertyType);
            var assign = Expression.Assign(Expression.Property(castObj, p), castValue);
            return Expression.Lambda<Action<object, object>>(assign, obj, value).Compile();
        }
    }
}
