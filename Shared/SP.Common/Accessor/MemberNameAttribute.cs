using System;

namespace SP.Common.Accessor
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public abstract class MemberNameAttribute : Attribute
    {
        public string Name { get; }
        protected MemberNameAttribute(string name) => Name = name;
    }


}
