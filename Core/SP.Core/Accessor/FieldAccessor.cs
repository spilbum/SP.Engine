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
            Info = f;

            if (CanGet)
                _getter = CreateGetter(f);
            if (CanSet)
                _setter = CreateSetter(f);
        }

        public string Name { get; }
        public Type Type { get; }
        public int Order { get; }
        public bool CanGet { get; }
        public bool CanSet { get; }
        public bool IgnoreGet { get; }
        public bool IgnoreSet { get; }
        public MemberInfo Info { get; }
        
        public object GetValue(object instance) => _getter(instance);
        public void SetValue(object instance, object value) => _setter(instance, value);

        private static Action<object, object> CreateSetter(FieldInfo f)
        {
            var objParam = Expression.Parameter(typeof(object), "obj");
            var valueParam = Expression.Parameter(typeof(object), "value");

            var target = Expression.Field(Expression.Convert(objParam, f.DeclaringType!), f);
            var type = f.FieldType;
            var assign = Expression.Assign(target, Expression.Convert(valueParam, type));

            return Expression.Lambda<Action<object, object>>(assign, objParam, valueParam).Compile();
        }

        private static Func<object, object> CreateGetter(FieldInfo f)
        {
            var objParam = Expression.Parameter(typeof(object), "obj");
            var body = Expression.Field(Expression.Convert(objParam, f.DeclaringType!), f);
            
            var convert = Expression.Convert(body, typeof(object));
            return Expression.Lambda<Func<object, object>>(convert, objParam).Compile();
        }
    }
}
