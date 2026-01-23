using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace SP.Core.Accessor
{
    public class RuntimeTypeAccessor
    {
        private static readonly ConcurrentDictionary<Type, RuntimeTypeAccessor> Cached =
            new ConcurrentDictionary<Type, RuntimeTypeAccessor>();

        private RuntimeTypeAccessor(Type type)
        {
            var members = new List<IMemberAccessor>();

            foreach (var member in type.GetMembers(BindingFlags.Instance | BindingFlags.Public))
                switch (member)
                {
                    case FieldInfo field:
                        members.Add(new FieldAccessor(field));
                        break;
                    case PropertyInfo property:
                        members.Add(new PropertyAccessor(property));
                        break;
                }

            members.Sort((a, b) => a.Order.CompareTo(b.Order));

            Name = type.Name;
            Members = members;
        }

        public string Name { get; }
        public IReadOnlyList<IMemberAccessor> Members { get; }

        public static RuntimeTypeAccessor GetOrCreate(Type type)
        {
            return Cached.GetOrAdd(type, t => new RuntimeTypeAccessor(t));
        }
    }
}
