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
            // 프로퍼티 검색
            var list = (from property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                where property.CanRead || property.CanWrite
                select new PropertyAccessor(property)).Cast<IMemberAccessor>().ToList();
            
            // 필드 검색
            list.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(field => new FieldAccessor(field)));

            if (list.Count == 0)
                throw new InvalidOperationException($"Invalid type: {type.FullName}");

            Name = type.Name.Replace("C", "");
            Members = list;
            _memberMap = list.ToDictionary(m => m.Name);
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


