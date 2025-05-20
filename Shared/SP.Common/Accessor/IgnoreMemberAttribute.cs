using System;

namespace SP.Common.Accessor
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class IgnoreMemberAttribute : Attribute
    {
        public bool IgnoreOnRead { get; }
        public bool IgnoreOnWrite { get; }

        public IgnoreMemberAttribute(bool ignoreOnRead = true, bool ignoreOnWrite = true)
        {
            IgnoreOnRead = ignoreOnRead;
            IgnoreOnWrite = ignoreOnWrite;
        }
    }
}
