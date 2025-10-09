using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SP.Common.Accessor
{
    public class RuntimeTypeAccessor
    {
        private static readonly ConcurrentDictionary<Type, RuntimeTypeAccessor> Cached =
            new ConcurrentDictionary<Type, RuntimeTypeAccessor>();

        public string Name { get; }
        public IReadOnlyList<IMemberAccessor> Members { get; }

        private readonly Dictionary<string, IMemberAccessor> _memberMap;

        private RuntimeTypeAccessor(Type type)
        {
            var members = new List<IMemberAccessor>();
            foreach (var m in type.GetMembers(BindingFlags.Instance | BindingFlags.Public))
            {
                switch (m)
                {
                    case PropertyInfo p when !p.CanRead || !p.CanWrite:
                        continue;
                    case PropertyInfo p when p.GetCustomAttribute<MemberIgnoreAttribute>() != null:
                        continue;
                    case PropertyInfo p:
                        members.Add(new PropertyAccessor(p));
                        break;
                    case FieldInfo f when f.IsInitOnly:
                        continue;
                    case FieldInfo f when f.GetCustomAttribute<MemberIgnoreAttribute>() != null:
                        continue;
                    case FieldInfo f:
                        members.Add(new FieldAccessor(f));
                        break;
                }
            }

            members.Sort((a, b) => a.Order.CompareTo(b.Order));
            
            Name = type.Name;
            Members = members;
            _memberMap = members.ToDictionary(m => m.Name);
        }
        


        public static RuntimeTypeAccessor GetOrCreate(Type type)
            => Cached.GetOrAdd(type, t => new RuntimeTypeAccessor(t));
        
        public bool HasMember(string name)
            => _memberMap.ContainsKey(name);
        
        public object this[object instance, string name]
        {
            get => _memberMap.TryGetValue(name, out var accessor)
                ? accessor.GetValue(instance)
                : throw new KeyNotFoundException($"No such member: {name}");
            set
            {
                if (_memberMap.TryGetValue(name, out var accessor))
                    accessor.SetValue(instance, value);
                else
                    throw new KeyNotFoundException($"No such member: {name}");
            }
        }
    }
}


