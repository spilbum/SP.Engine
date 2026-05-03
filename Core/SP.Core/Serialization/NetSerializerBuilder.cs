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
        
        private static readonly MethodInfo WriteBool = typeof(NetWriter).GetMethod(nameof(NetWriter.WriteBool));
        private static readonly MethodInfo ReadBool = typeof(NetReader).GetMethod(nameof(NetReader.ReadBool));

        public static SerializerPair Build(Type type)
        {
            var writer = BuildWriter(type);
            var reader = BuildReader(type);

            return new SerializerPair(
                reader.ObjectFn,
                writer.ObjectFn,
                reader.GenericFn,
                writer.GenericFn);
        }
        
        private static (object GenericFn, SerializerPair.WriteFn ObjectFn) BuildWriter(Type type)
        {
            var w = Expression.Parameter(typeof(NetWriter).MakeByRefType(), "w");

            var valParam = Expression.Parameter(type, "val");
            var writeBlock = new List<Expression>
            {
                Expression.Call(w, WriteBool, Expression.Constant(true))
            };
            
            var accessor = RuntimeTypeAccessor.GetOrCreate(type);
            foreach (var member in accessor.Members.Where(m => m.CanGet && !m.IgnoreGet))
            {
                var memberAccess = Expression.MakeMemberAccess(valParam, member.Info);
                Expression writeCall;
                
                var writeMethod = GetNetWriterMethod(member.Type);
                if (writeMethod != null)
                {
                    writeCall = Expression.Call(w, writeMethod, memberAccess);
                }
                else if (member.Type.IsPrimitive || member.Type.IsEnum)
                {
                    writeCall = Expression.Call(w, WriteGeneric.MakeGenericMethod(member.Type), memberAccess);
                }
                else
                {
                    writeCall = Expression.Call(SerializeGeneric.MakeGenericMethod(member.Type), w, memberAccess);
                }

                writeBlock.Add(writeCall);
            }

            var coreBlock = Expression.Block(writeBlock);
            var genType = typeof(SerializerPair.WriteGenericFn<>).MakeGenericType(type);
            var genericLambda = Expression.Lambda(genType, coreBlock, w, valParam).Compile();

            var objParam = Expression.Parameter(typeof(object), "obj");
            var objBody = Expression.IfThenElse(
                Expression.Equal(objParam, Expression.Constant(null)),
                Expression.Call(w, WriteBool, Expression.Constant(false)),
                Expression.Invoke(Expression.Constant(genericLambda), w, Expression.Convert(objParam, type))
            );

            var objectLambda = Expression.Lambda<SerializerPair.WriteFn>(objBody, w, objParam).Compile();
            return (genericLambda, objectLambda);
        }

        private static (object GenericFn, SerializerPair.ReadFn ObjectFn) BuildReader(Type type)
        {
            var r = Expression.Parameter(typeof(NetReader).MakeByRefType(), "r");
            var instance = Expression.Variable(type, "instance");
            var ctor = type.GetConstructor(Type.EmptyTypes) ??
                       throw new InvalidOperationException($"{type.Name} needs ctor");
            var readBlockExprs = new List<Expression> { Expression.Assign(instance, Expression.New(ctor)) };
            
            var accessor = RuntimeTypeAccessor.GetOrCreate(type);
            foreach (var member in accessor.Members.Where(m => m.CanSet && !m.IgnoreSet))
            {
                Expression readCall;
                var readerMethod = GetNetReaderMethod(member.Type);
                if (readerMethod != null)
                {
                    readCall = Expression.Call(r, readerMethod);
                }
                else if (member.Type.IsPrimitive || member.Type.IsEnum)
                {
                    readCall = Expression.Call(r, ReadGeneric.MakeGenericMethod(member.Type));
                }
                else
                {
                    readCall = Expression.Call(DeserializeGeneric.MakeGenericMethod(member.Type), r);
                }
                
                readBlockExprs.Add(Expression.Assign(Expression.MakeMemberAccess(instance, member.Info), readCall));
            }

            readBlockExprs.Add(instance);
            var readDataBlock = Expression.Block(new[] { instance }, readBlockExprs);

            var genericBody = Expression.Condition(
                Expression.Call(r, ReadBool),
                readDataBlock,
                Expression.Default(type)
            );
           
            var genType = typeof(SerializerPair.ReadGenericFn<>).MakeGenericType(type);
            var genericLambda = Expression.Lambda(genType, genericBody, r).Compile();

            var objBody = Expression.Convert(
                    Expression.Invoke(Expression.Constant(genericLambda), r),
                    typeof(object)
            );
            
            var objectLambda = Expression.Lambda<SerializerPair.ReadFn>(objBody, r).Compile();
            return (genericLambda, objectLambda);
        }

        private static MethodInfo GetNetWriterMethod(Type t)
        {
            if (t == typeof(bool)) return typeof(NetWriter).GetMethod(nameof(NetWriter.WriteBool));
            if (t == typeof(byte)) return typeof(NetWriter).GetMethod(nameof(NetWriter.WriteByte));
            if (t == typeof(sbyte)) return typeof(NetWriter).GetMethod(nameof(NetWriter.WriteSByte));
            if (t == typeof(short)) return typeof(NetWriter).GetMethod(nameof(NetWriter.WriteInt16));
            if (t == typeof(ushort)) return typeof(NetWriter).GetMethod(nameof(NetWriter.WriteUInt16));
            if (t == typeof(int)) return typeof(NetWriter).GetMethod(nameof(NetWriter.WriteInt32));
            if (t == typeof(uint)) return typeof(NetWriter).GetMethod(nameof(NetWriter.WriteUInt32));
            if (t == typeof(long)) return typeof(NetWriter).GetMethod(nameof(NetWriter.WriteInt64));
            if (t == typeof(ulong)) return typeof(NetWriter).GetMethod(nameof(NetWriter.WriteUInt64));
            if (t == typeof(float)) return typeof(NetWriter).GetMethod(nameof(NetWriter.WriteSingle));
            if (t == typeof(double)) return typeof(NetWriter).GetMethod(nameof(NetWriter.WriteDouble));
            return null;
        }

        private static MethodInfo GetNetReaderMethod(Type t)
        {
            if (t == typeof(bool)) return typeof(NetReader).GetMethod(nameof(NetReader.ReadBool));
            if (t == typeof(byte)) return typeof(NetReader).GetMethod(nameof(NetReader.ReadByte));
            if (t == typeof(sbyte)) return typeof(NetReader).GetMethod(nameof(NetReader.ReadSByte));
            if (t == typeof(short)) return typeof(NetReader).GetMethod(nameof(NetReader.ReadInt16));
            if (t == typeof(ushort)) return typeof(NetReader).GetMethod(nameof(NetReader.ReadUInt16));
            if (t == typeof(int)) return typeof(NetReader).GetMethod(nameof(NetReader.ReadInt32));
            if (t == typeof(uint)) return typeof(NetReader).GetMethod(nameof(NetReader.ReadUInt32));
            if (t == typeof(long)) return typeof(NetReader).GetMethod(nameof(NetReader.ReadInt64));
            if (t == typeof(ulong)) return typeof(NetReader).GetMethod(nameof(NetReader.ReadUInt64));
            if (t == typeof(float)) return typeof(NetReader).GetMethod(nameof(NetReader.ReadSingle));
            if (t == typeof(double)) return typeof(NetReader).GetMethod(nameof(NetReader.ReadDouble));
            return null;
        }
    }
}
    
