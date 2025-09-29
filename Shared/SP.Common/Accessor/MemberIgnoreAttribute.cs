using System;

namespace SP.Common.Accessor
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class MemberIgnoreAttribute : Attribute { }
}
