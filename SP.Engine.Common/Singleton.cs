using System;

namespace SP.Engine.Common
{
    public class Singleton<T> where T : class, new()
    {
        private static readonly Lazy<T> InstanceLazy;
    
        static Singleton()
        {
            InstanceLazy = new Lazy<T>(() => CreateInstance());
        }

        public static Func<T> CreateInstance { private get; set; } = Activator.CreateInstance<T>;
        public static T Instance => InstanceLazy.Value;
    }
}
