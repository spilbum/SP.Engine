using System;

namespace SP.Common.Accessor
{
    public interface IMemberAccessor
    {
        string Name { get; }
        Type Type { get; }
        bool CanRead { get; }
        bool CanWrite { get; }
        
        object GetValue(object instance);
        void SetValue(object instance, object value);
    }
}
