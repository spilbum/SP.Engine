using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using SP.Core.Accessor;

namespace SP.Core.Serialization
{
    internal static class NetSerializerBuilder
    {
        // public void Write<T>(T value) 
        private static readonly MethodInfo WriteGeneric = typeof(NetWriter)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Write" && m.IsGenericMethod && m.GetParameters().Length == 1);

        // public T Read<T>()
        private static readonly MethodInfo ReadGeneric = typeof(NetReader)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Read" && m.IsGenericMethod && m.GetParameters().Length == 0);

        // public static void Serialize<T>(ref NetWriter w, T value)
        private static readonly MethodInfo SerializeGeneric = typeof(NetSerializer)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "Serialize" && m.IsGenericMethod && m.GetParameters().Length == 2);
        
        // public static T Deserialize<T>(ref NetReader r)
        private static readonly MethodInfo DeserializeGeneric = typeof(NetSerializer)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "Deserialize" && m.IsGenericMethod && m.GetParameters().Length == 1);

        private static readonly MethodInfo WriteBool =
            typeof(NetWriter).GetMethod(nameof(NetWriter.WriteBool));

        private static readonly MethodInfo ReadBool =
            typeof(NetReader).GetMethod(nameof(NetReader.ReadBool));

        public static NetSerializer.SerializerPair.WriteFn BuildWriter(Type type)
        {
            var w = Expression.Parameter(typeof(NetWriter).MakeByRefType(), "w");
            var obj = Expression.Parameter(typeof(object), "obj");

            // 로컬 변수: ((Type)obj)
            var typedObj = Expression.Variable(type, "val");
            var assign = Expression.Assign(typedObj, Expression.Convert(obj, type));

            var writeBlock = new List<Expression>
            {
                assign,
                Expression.Call(w, WriteBool, Expression.Constant(true))
            };

            var accessor = RuntimeTypeAccessor.GetOrCreate(type);
            foreach (var member in accessor.Members)
            {
                if (!member.CanGet || member.IgnoreGet) continue;

                var memberAccess = Expression.MakeMemberAccess(typedObj, member.Info);

                var writeCall = IsFastPath(member.Type)
                    // w.Write<T>(val.Member)
                    ? Expression.Call(w, WriteGeneric.MakeGenericMethod(member.Type), memberAccess)
                    // NetSerializer.Serialize<T>(ref w, val.Member)
                    : Expression.Call(SerializeGeneric.MakeGenericMethod(member.Type), w, memberAccess);

                writeBlock.Add(writeCall);
            }

            // if (obj == null) { w.WriteBool(false); } else { ... }
            var body = Expression.IfThenElse(
                Expression.Equal(obj, Expression.Constant(null)),
                Expression.Call(w, WriteBool, Expression.Constant(false)),
                Expression.Block(new[] { typedObj }, writeBlock));

            return Expression.Lambda<NetSerializer.SerializerPair.WriteFn>(body, w, obj).Compile();
        }

        public static NetSerializer.SerializerPair.ReadFn BuildReader(Type type)
        {
            var r = Expression.Parameter(typeof(NetReader).MakeByRefType(), "r");

            var ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor == null)
                throw new InvalidOperationException($"Class '{type.Name}' requires a parameterless constructor.");

            var instance = Expression.Variable(type, "instance");
            var assign = Expression.Assign(instance, Expression.New(ctor));

            var readBlockExprs = new List<Expression> { assign };

            var accessor = RuntimeTypeAccessor.GetOrCreate(type);
            foreach (var member in accessor.Members)
            {
                if (!member.CanSet || member.IgnoreSet) continue;
                
                var readCall = IsFastPath(member.Type) 
                    // r.Read<T>() 
                    ? Expression.Call(r, ReadGeneric.MakeGenericMethod(member.Type))
                    // NetSerializer.Deserialize<T>(ref r)
                    : Expression.Call(DeserializeGeneric.MakeGenericMethod(member.Type), r);

                readBlockExprs.Add(Expression.Assign(Expression.MakeMemberAccess(instance, member.Info), readCall));
            }

            // return (object)instance;
            readBlockExprs.Add(Expression.Convert(instance, typeof(object)));

            var readBlock = Expression.Block(new[] { instance }, readBlockExprs);

            // if (r.ReadBool()) { return readBlock; } else { return null; }
            var body = Expression.Condition(
                Expression.Call(r, ReadBool),
                readBlock,
                Expression.Constant(null)
            );

            return Expression.Lambda<NetSerializer.SerializerPair.ReadFn>(body, r).Compile();
        }
        
        private static bool IsFastPath(Type t) => t.IsPrimitive || t.IsEnum;
    }
}
    
