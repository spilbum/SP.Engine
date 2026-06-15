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
    }
}
