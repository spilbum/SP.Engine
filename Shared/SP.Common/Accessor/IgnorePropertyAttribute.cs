using System;

namespace SP.Common.Accessor
{
    [AttributeUsage(AttributeTargets.Property)]
    public class IgnorePropertyAttribute : Attribute
    {
        public bool IgnoreOnRead { get; set; }
        public bool IgnoreOnWrite { get; set; }

        public IgnorePropertyAttribute()
            : this(true, true)
        {
            
        }
        public IgnorePropertyAttribute(bool ignoreOnRead, bool ignoreOnWrite)
        {
            IgnoreOnRead = ignoreOnRead;
            IgnoreOnWrite = ignoreOnWrite;
        }
    }
}
