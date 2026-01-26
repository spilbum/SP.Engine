using System;
using System.Reflection;

namespace SP.Core.Accessor
{
    public interface IMemberAccessor
    {
        string Name { get; }
        Type Type { get; }
        int Order { get; }
        bool CanGet { get; }
        bool CanSet { get; }
        bool IgnoreGet { get; }
        bool IgnoreSet { get; }
        MemberInfo Info { get; }

        object GetValue(object instance);
        void SetValue(object instance, object value);
    }

    public static class MemberAccessorExtensions
    {
        public static bool IsNullable(this IMemberAccessor accessor)
        {
            if (!accessor.Type.IsValueType)
                return true;
            return Nullable.GetUnderlyingType(accessor.Type) != null;
        }
    }
}
