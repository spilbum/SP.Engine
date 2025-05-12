using System;

namespace SP.Common.Accessor
{
    [AttributeUsage(AttributeTargets.Property)]
    public abstract class PropertyAttribute : Attribute
    {
        public string Name { get; set; }

        protected PropertyAttribute(string name)
        {
            Name = name;
        }
    }
}
