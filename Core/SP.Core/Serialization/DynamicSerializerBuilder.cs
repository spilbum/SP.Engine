using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using SP.Core.Accessor;

namespace SP.Core.Serialization
{
    internal static class DynamicSerializerBuilder
    {
        // NetWriter.Write<T>(T value);
        private static readonly MethodInfo WriteGeneric = typeof(NetWriter)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Write" && m.IsGenericMethod && m.GetParameters().Length == 1);

        // T NetReader.Read<T>();
        private static readonly MethodInfo ReadGeneric = typeof(NetReader)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Read" && m.IsGenericMethod && m.GetParameters().Length == 0);
        
        // NetSerializer.Serialize<T>(ref NetWriter w, T value)
        private static readonly MethodInfo SerializeGeneric = typeof(NetSerializer)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "Serialize" && m.IsGenericMethod && m.GetParameters().Length == 2);
        
        // T NetSerializer.Deserialize<T>(ref NetReader r)
        private static readonly MethodInfo DeserializeGeneric = typeof(NetSerializer)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "Deserialize" && m.IsGenericMethod && m.GetParameters().Length == 1);
        
        private static readonly MethodInfo WriteBool = typeof(NetWriter).GetMethod(nameof(NetWriter.WriteBool));
        private static readonly MethodInfo ReadBool = typeof(NetReader).GetMethod(nameof(NetReader.ReadBool));
        private static readonly MethodInfo WriteInt64 = typeof(NetWriter).GetMethod(nameof(NetWriter.WriteInt64));
        private static readonly MethodInfo ReadInt64 = typeof(NetReader).GetMethod(nameof(NetReader.ReadInt64));
        private static readonly PropertyInfo DateTimeTicks = typeof(DateTime).GetProperty(nameof(DateTime.Ticks));
        private static readonly ConstructorInfo DateTimeCtor = typeof(DateTime).GetConstructor(new[] { typeof(long) });
        
        public static TypeSerializer Build(Type type)
        {
            return new TypeSerializer(
                CompileReader(type), 
                CompileWriter(type),
                CompilePopulate(type),
                CompileReset(type)
            );
        }
        
        private static object CompileWriter(Type type)
        {
            var writerParam = Expression.Parameter(typeof(NetWriter).MakeByRefType(), "writer");
            var valueParam = Expression.Parameter(type, "value");
            var bodyBlock = new List<Expression>();
            
            foreach (var member in RuntimeTypeAccessor
                         .GetOrCreate(type)
                         .Members
                         .Where(m => m.CanGet && !m.IgnoreGet))
            {
                var memberAccess = Expression.MakeMemberAccess(valueParam, member.Info);

                if (member.Type.IsEnum)
                {
                    // Enum인 경우 기저 타입(int, byte...)으로 강제 캐스팅 후 NetWriter.Write<underlyingType>() 호출
                    var underlyingType = Enum.GetUnderlyingType(member.Type);
                    var casted = Expression.Convert(memberAccess, underlyingType);
                    bodyBlock.Add(Expression.Call(writerParam, WriteGeneric.MakeGenericMethod(underlyingType), casted));
                }
                else if (member.Type == typeof(DateTime))
                {
                    // DateTime인 경우 Ticks(long)를 추출하여 밀어넣음
                    var ticks = Expression.Property(memberAccess, DateTimeTicks);
                    bodyBlock.Add(Expression.Call(writerParam, WriteInt64, ticks));
                }
                else if (member.Type.IsPrimitive)
                {
                    bodyBlock.Add(Expression.Call(writerParam, WriteGeneric.MakeGenericMethod(member.Type), memberAccess));
                }
                else if (member.Type.IsValueType)
                {
                    // struct 
                    bodyBlock.Add(Expression.Call(SerializeGeneric.MakeGenericMethod(member.Type), writerParam, memberAccess));
                }
                else
                {
                    // 참조 타입 처리
                    var writeNull = Expression.Call(writerParam, WriteBool, Expression.Constant(false));
                    var writeNotNull = Expression.Call(writerParam, WriteBool, Expression.Constant(true));
                    var writeContent = Expression.Call(SerializeGeneric.MakeGenericMethod(member.Type), writerParam, memberAccess);
                    
                    bodyBlock.Add(Expression.IfThenElse(
                        Expression.Equal(memberAccess, Expression.Constant(null, member.Type)),
                        writeNull,
                        Expression.Block(writeNotNull, writeContent)));                    
                }
            }
            
            if (bodyBlock.Count == 0) bodyBlock.Add(Expression.Empty());

            return Expression.Lambda(
                typeof(TypeSerializer.WriteDelegate<>).MakeGenericType(type),
                Expression.Block(bodyBlock),
                writerParam,
                valueParam
            ).Compile();
        }

        private static object CompileReader(Type type)
        {
            var readerParam = Expression.Parameter(typeof(NetReader).MakeByRefType(), "reader");
            
            var resultVar = Expression.Variable(type, "result");
            var bodyBlock = new List<Expression>
            {
                // 인스턴스 생성 및 할당 (result = new T())
                Expression.Assign(resultVar, Expression.New(type))
            };

            var accessor = RuntimeTypeAccessor.GetOrCreate(type);
            foreach (var member in accessor.Members.Where(m => m.CanSet && !m.IgnoreSet))
            {
                var memberAccess = Expression.MakeMemberAccess(resultVar, member.Info);

                if (member.Type.IsEnum)
                {
                    var underlyingType = Enum.GetUnderlyingType(member.Type);
                    var readCall = Expression.Call(readerParam, ReadGeneric.MakeGenericMethod(underlyingType));
                    bodyBlock.Add(Expression.Assign(memberAccess, Expression.Convert(readCall, member.Type)));
                }
                else if (member.Type == typeof(DateTime))
                {
                    var ticks = Expression.Call(readerParam, ReadInt64);
                    bodyBlock.Add(Expression.Assign(memberAccess, Expression.New(DateTimeCtor, ticks)));
                }
                else if (member.Type.IsPrimitive)
                {
                    var readCall = Expression.Call(readerParam, ReadGeneric.MakeGenericMethod(member.Type));
                    bodyBlock.Add(Expression.Assign(memberAccess, readCall));
                }
                else if (member.Type.IsValueType)
                {
                    var deserializeCall = Expression.Call(DeserializeGeneric.MakeGenericMethod(member.Type), readerParam);
                    bodyBlock.Add(Expression.Assign(memberAccess, deserializeCall));
                }
                else
                {
                    var deserializeCall = Expression.Call(DeserializeGeneric.MakeGenericMethod(member.Type), readerParam);
                    bodyBlock.Add(Expression.IfThenElse(
                        Expression.Call(readerParam, ReadBool),
                        Expression.Assign(memberAccess, deserializeCall),
                        Expression.Assign(memberAccess, Expression.Constant(null, member.Type))
                    ));
                }
            }
            
            bodyBlock.Add(resultVar);

            return Expression.Lambda(
                typeof(TypeSerializer.ReadDelegate<>).MakeGenericType(type),
                Expression.Block(new[] { resultVar }, bodyBlock),
                readerParam
            ).Compile();
        }
        
        private static object CompilePopulate(Type type)
        {
            var readerParam = Expression.Parameter(typeof(NetReader).MakeByRefType(), "reader");
            var instanceParam = Expression.Parameter(type, "instance");
            var bodyBlock = new List<Expression>();
            
            foreach (var member in RuntimeTypeAccessor
                         .GetOrCreate(type)
                         .Members
                         .Where(m => m.CanSet && !m.IgnoreSet))
            {
                var memberAccess = Expression.MakeMemberAccess(instanceParam, member.Info);

                if (member.Type.IsEnum)
                {
                    // 스트림에서 기저 타입을 먼저 Read한 뒤, Enum 타입으로 명시적 캐스팅하여 할당
                    var underlyingType = Enum.GetUnderlyingType(member.Type);
                    var readCall = Expression.Call(readerParam, ReadGeneric.MakeGenericMethod(underlyingType));
                    bodyBlock.Add(Expression.Assign(memberAccess, Expression.Convert(readCall, member.Type)));
                }
                else if (member.Type == typeof(DateTime))
                {
                    // 스트림에서 Int64(Ticks)를 읽어와 DateTime 객체를 생성 후 할당
                    var ticks = Expression.Call(readerParam, ReadInt64);
                    bodyBlock.Add(Expression.Assign(memberAccess, Expression.New(DateTimeCtor, ticks)));
                }
                else if (member.Type.IsPrimitive)
                {
                    bodyBlock.Add(Expression.Assign(memberAccess, Expression.Call(readerParam, ReadGeneric.MakeGenericMethod(member.Type))));
                }
                else if (member.Type.IsValueType)
                {
                    var deserializeCall = Expression.Call(DeserializeGeneric.MakeGenericMethod(member.Type), readerParam);
                    bodyBlock.Add(Expression.Assign(memberAccess, deserializeCall));
                }
                else
                {
                    var deserializeCall = Expression.Call(DeserializeGeneric.MakeGenericMethod(member.Type), readerParam);
                    bodyBlock.Add(Expression.IfThenElse(
                        Expression.Call(readerParam, ReadBool),
                        Expression.Assign(memberAccess, deserializeCall),
                        Expression.Assign(memberAccess, Expression.Constant(null, member.Type))));
                }
            }

            if (bodyBlock.Count == 0) bodyBlock.Add(Expression.Empty());

            return Expression.Lambda(
                typeof(TypeSerializer.PopulateDelegate<>).MakeGenericType(type),
                Expression.Block(bodyBlock),
                readerParam,
                instanceParam
            ).Compile();
        }
        
        private static object CompileReset(Type type)
        {
            var instanceParam = Expression.Parameter(type, "instance");
            var bodyBlock = new List<Expression>();
            
            var accessor = RuntimeTypeAccessor.GetOrCreate(type);
            foreach (var member in accessor.Members.Where(m => m.CanSet && !m.IgnoreSet))
            {
                var memberAccess = Expression.MakeMemberAccess(instanceParam, member.Info);

                if (member.Type.IsGenericType &&
                    (member.Type.GetGenericTypeDefinition() == typeof(List<>) ||
                    member.Type.GetGenericTypeDefinition() == typeof(Dictionary<,>)))
                {
                    var clearMethod = member.Type.GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);
                    if (clearMethod == null) continue;
                    
                    var methodCall = Expression.Call(memberAccess, clearMethod);
                    bodyBlock.Add(Expression.IfThen(Expression.NotEqual(memberAccess, Expression.Constant(null)), methodCall));
                }
                else
                {
                    // 디폴트 값으로 설정
                    bodyBlock.Add(Expression.Assign(memberAccess, Expression.Default(member.Type)));
                }
            }

            if (bodyBlock.Count == 0) bodyBlock.Add(Expression.Empty());

            return Expression.Lambda(
                typeof(TypeSerializer.ResetDelegate<>).MakeGenericType(type),
                Expression.Block(bodyBlock),
                instanceParam
            ).Compile();
        }
    }
}
