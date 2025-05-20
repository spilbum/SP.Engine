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

        public IReadOnlyList<IMemberAccessor> Members { get; }

        private RuntimeTypeAccessor(Type type)
        {
            var list = new List<IMemberAccessor>();
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead && !property.CanWrite) continue;
                list.Add(new PropertyAccessor(property));
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                list.Add(new FieldAccessor(field));
            }
            
            Members = list;
        }

        public static RuntimeTypeAccessor GetOrCreate(Type type)
            => Cached.GetOrAdd(type, t => new RuntimeTypeAccessor(t));
        
        public object this[object instance, string name]
        {
            get => Members.First(x => x.Name == name).GetValue(instance);
            set => Members.First(x => x.Name == name).SetValue(instance, value);
        }
    }
}


