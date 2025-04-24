using System;
using System.Reflection;
using System.Reflection.Emit;

namespace SP.Engine.Common.Accessor
{
    public static class PropertyAccessor
    {
        public static Func<object, object> CreateGetter(PropertyInfo propertyInfo)
        {
            if (!propertyInfo.CanRead || propertyInfo.GetGetMethod() == null)
                throw new ArgumentException($"Property {propertyInfo.Name} does not have a getter.");

            var method = new DynamicMethod(
                $"Get_{propertyInfo.Name}",
                typeof(object),
                new[] { typeof(object) },
                propertyInfo.DeclaringType?.Module);

            var il = method.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0); // 객체 로드
            il.Emit(OpCodes.Castclass, propertyInfo.DeclaringType); // 객체 타입 캐스팅
            il.EmitCall(OpCodes.Callvirt, propertyInfo.GetGetMethod(), null); // Getter 호출

            if (propertyInfo.PropertyType.IsValueType)
                il.Emit(OpCodes.Box, propertyInfo.PropertyType); // 값 타입이면 Boxing

            il.Emit(OpCodes.Ret); // 반환

            return (Func<object, object>)method.CreateDelegate(typeof(Func<object, object>));
        }

        public static Action<object, object> CreateSetter(PropertyInfo propertyInfo)
        {
            if (!propertyInfo.CanWrite || propertyInfo.GetSetMethod() == null)
                throw new ArgumentException($"Property {propertyInfo.Name} does not have a setter.");

            var method = new DynamicMethod(
                $"Set_{propertyInfo.Name}",
                null,
                new[] { typeof(object), typeof(object) },
                propertyInfo.DeclaringType?.Module);

            var il = method.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0); // 객체 로드
            il.Emit(OpCodes.Castclass, propertyInfo.DeclaringType); // 객체 타입 캐스팅
            il.Emit(OpCodes.Ldarg_1); // 값 로드

            if (Nullable.GetUnderlyingType(propertyInfo.PropertyType) != null)
            {
                il.Emit(OpCodes.Unbox_Any, propertyInfo.PropertyType);
            }
            else
            {
                il.Emit(propertyInfo.PropertyType.IsValueType
                    ? OpCodes.Unbox_Any
                    : OpCodes.Castclass, propertyInfo.PropertyType);
            }

            il.EmitCall(OpCodes.Callvirt, propertyInfo.GetSetMethod(), null); // Setter 호출
            il.Emit(OpCodes.Ret); // 반환

            return (Action<object, object>)method.CreateDelegate(typeof(Action<object, object>));
        }
    }
}
