using System;

namespace SP.Core.Accessor
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class MemberAttribute : Attribute
    {
        public MemberAttribute(string name = null)
        {
            Name = name;
        }

        public string Name { get; }
        public int Order { get; set; }
        public bool IgnoreGet { get; set; }
        public bool IgnoreSet { get; set; }
    }
}
