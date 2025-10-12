using System;

namespace SP.Common.Accessor
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public abstract class MemberOrderAttribute : Attribute
    {
        public int Order { get; }
        protected MemberOrderAttribute(int order) => Order = order;
    }
}
