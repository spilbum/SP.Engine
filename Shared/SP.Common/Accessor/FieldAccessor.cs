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
        public int Order { get; }

        public FieldAccessor(FieldInfo f)
        {
            Name = GetName(f);
            Type = f.FieldType;
            Order = GetOrder(f);
            _getter = BuildGetter(f);
            _setter = BuildSetter(f);
        }

        private static string GetName(FieldInfo f)
        {
            var attr = f.GetCustomAttribute<MemberNameAttribute>();
            return attr?.Name ?? f.Name;
        }
        
        private static int GetOrder(FieldInfo f)
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

        private static Func<object, object> BuildGetter(FieldInfo f)
        {
            if (f.DeclaringType == null)
                throw new ArgumentException("Field declaring type is null", nameof(f));
            
            var obj = Expression.Parameter(typeof(object), "obj");
            var cast = Expression.Convert(obj, f.DeclaringType);
            var fld = Expression.Field(cast, f);
            var box = Expression.Convert(fld, typeof(object));
            return Expression.Lambda<Func<object, object>>(box, obj).Compile();
        }

        private static Action<object, object> BuildSetter(FieldInfo f)
        {
            if (f.DeclaringType == null)
                throw new ArgumentException("Field declaring type is null", nameof(f));
            
            var obj = Expression.Parameter(typeof(object), "obj");
            var value = Expression.Parameter(typeof(object), "value");
            var castObj = Expression.Convert(obj, f.DeclaringType);
            var castValue = Expression.Convert(value, f.FieldType);
            var assign = Expression.Assign(Expression.Field(castObj, f), castValue);
            return Expression.Lambda<Action<object, object>>(assign, obj, value).Compile();
        }
    }
}
