using System;

namespace SP.Common.Accessor
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public abstract class MemberAttribute : Attribute
    {
        public string Name { get; set; }
        protected MemberAttribute(string name) => Name = name;
    }
}
