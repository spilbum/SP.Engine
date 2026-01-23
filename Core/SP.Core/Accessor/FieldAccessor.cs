using System;
using System.Linq.Expressions;
using System.Reflection;

namespace SP.Core.Accessor
{
    public class FieldAccessor : IMemberAccessor
    {
        private readonly Func<object, object> _getter;
        private readonly Action<object, object> _setter;

        public FieldAccessor(FieldInfo f)
        {
            var attr = f.GetCustomAttribute<MemberAttribute>();

            Name = attr?.Name ?? f.Name;
            Type = f.FieldType;
            Order = attr?.Order ?? int.MaxValue;
            IgnoreGet = attr?.IgnoreGet ?? false;
            IgnoreSet = attr?.IgnoreSet ?? false;
            CanGet = true;
            CanSet = !f.IsInitOnly && !f.IsLiteral;
            if (CanGet)
                _getter = BuildGetter(f);
            if (CanSet)
                _setter = BuildSetter(f);
        }

        public string Name { get; }
        public Type Type { get; }
        public int Order { get; }
        public bool CanGet { get; }
        public bool CanSet { get; }
        public bool IgnoreGet { get; }
        public bool IgnoreSet { get; }

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

        private static Func<object, object> BuildGetter(FieldInfo f)
        {
            var obj = Expression.Parameter(typeof(object), "obj");
            var castObj = Expression.Convert(obj, f.DeclaringType!);
            var box = Expression.Convert(Expression.Field(castObj, f), typeof(object));
            return Expression.Lambda<Func<object, object>>(box, obj).Compile();
        }

        private static Action<object, object> BuildSetter(FieldInfo f)
        {
            var obj = Expression.Parameter(typeof(object), "obj");
            var value = Expression.Parameter(typeof(object), "value");
            var castObj = Expression.Convert(obj, f.DeclaringType!);
            var castVal = Expression.Convert(value, f.FieldType);
            var assign = Expression.Assign(Expression.Field(castObj, f), castVal);
            return Expression.Lambda<Action<object, object>>(assign, obj, value).Compile();
        }
    }
}
