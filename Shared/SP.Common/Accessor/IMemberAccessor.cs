using System;

namespace SP.Common.Accessor
{
    public interface IMemberAccessor
    {
        string Name { get; }
        Type Type { get; }
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
