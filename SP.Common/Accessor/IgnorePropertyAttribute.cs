using System;

namespace SP.Common.Accessor
{
    [AttributeUsage(AttributeTargets.Property)]
    public class IgnorePropertyAttribute : Attribute
    {
        public bool IgnoreOnRead { get; set; }
        public bool IgnoreOnWrite { get; set; }

        public IgnorePropertyAttribute(bool ignoreOnRead = true, bool ignoreOnWrite = true)
        {
            IgnoreOnRead = ignoreOnRead;
            IgnoreOnWrite = ignoreOnWrite;
        }
    }
}
