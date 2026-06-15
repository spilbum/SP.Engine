using System;
using System.Linq.Expressions;
using System.Reflection;

namespace SP.Core.Accessor
{
    public class PropertyAccessor : IMemberAccessor
    {
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
        }

        public string Name { get; }
        public Type Type { get; }
        public int Order { get; }
        public bool IgnoreGet { get; }
        public bool IgnoreSet { get; }
        public bool CanGet { get; }
        public bool CanSet { get; }
        public MemberInfo Info { get; }
    }
}
