using System;
using System.Linq.Expressions;
using System.Reflection;

namespace SP.Core.Accessor
{
    public class FieldAccessor : IMemberAccessor
    {
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
        }

        public string Name { get; }
        public Type Type { get; }
        public int Order { get; }
        public bool CanGet { get; }
        public bool CanSet { get; }
        public bool IgnoreGet { get; }
        public bool IgnoreSet { get; }
        public MemberInfo Info { get; }
    }
}
