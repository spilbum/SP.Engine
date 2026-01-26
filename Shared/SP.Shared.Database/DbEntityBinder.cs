using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using SP.Core.Accessor;

namespace SP.Shared.Database;

public static class DbEntityBinder
{
    private static readonly ConcurrentDictionary<Type, (Action<object, DbDataReader>, Action<object, DbCmd>)>
        Cache = new();

    public static (Action<object, DbDataReader> Read, Action<object, DbCmd> Write) Get(Type type)
        => Cache.GetOrAdd(type, t => (CompileRead(t), CompileWrite(t)));

    private static Action<object, DbDataReader> CompileRead(Type type)
    {
        var entityParam = Expression.Parameter(typeof(object), "entity");
        var readerParam = Expression.Parameter(typeof(DbDataReader), "reader");

        // 로컬 변수: ((Type)entity)
        var typedEntity = Expression.Variable(type, "target");
        var assignEntity = Expression.Assign(typedEntity, Expression.Convert(entityParam, type));
        
        var blocks = new List<Expression> { assignEntity };

        // 리플렉션 메소드 정보
        var readMethod = typeof(DbValueMapper).GetMethod(nameof(DbValueMapper.ReadValue));
        var hasColumnMethod = typeof(DbValueMapper).GetMethod(nameof(DbValueMapper.HasColumn));

        var accessor = RuntimeTypeAccessor.GetOrCreate(type);
        foreach (var member in accessor.Members)
        {
            if (!member.CanSet || member.IgnoreSet) continue;

            // 로직: if (HasColumn(reader, name)) { target.Prop = (Type)ReadValue(reader, name, type); }
            
            // ReadValue(reader, name, type)
            var callRead = Expression.Call(readMethod!, 
                readerParam, 
                Expression.Constant(member.Name), 
                Expression.Constant(member.Type)
            );

            // 값 할당 (Unboxing/Casting)
            var assign = Expression.Assign(
                Expression.MakeMemberAccess(typedEntity, member.Info),
                Expression.Convert(callRead, member.Type)
            );
            
            // 컬럼 존재 체크 후 할당
            var checkAndAssign = Expression.IfThen(
                Expression.Call(hasColumnMethod!, readerParam, Expression.Constant(member.Name)),
                assign
            );
            
            blocks.Add(checkAndAssign);
        }

        return Expression.Lambda<Action<object, DbDataReader>>(
            Expression.Block([typedEntity], blocks),
            entityParam, readerParam
        ).Compile();
    }

    private static Action<object, DbCmd> CompileWrite(Type type)
    {
        var entityParam = Expression.Parameter(typeof(object), "entity");
        var cmdParam = Expression.Parameter(typeof(DbCmd), "cmd");
        
        var typedEntity = Expression.Variable(type, "target");
        var assignEntity = Expression.Assign(typedEntity, Expression.Convert(entityParam, type));
        
        var blocks = new List<Expression> { assignEntity };

        var writeMethod = typeof(DbValueMapper).GetMethod(nameof(DbValueMapper.WriteValue));
        
        var accessor = RuntimeTypeAccessor.GetOrCreate(type);
        foreach (var member in accessor.Members)
        {
            if (!member.CanGet || member.IgnoreGet) continue;
            
            // 로직: WriteValue(cmd, name, target.Prop, type);
            
            // 값 읽기 (target.Prop) -> object 변환
            var propValue = Expression.Convert(
                Expression.MakeMemberAccess(typedEntity, member.Info),
                typeof(object)
            );

            // WriteValue 호출
            var callWrite = Expression.Call(writeMethod!,
                cmdParam,
                Expression.Constant(member.Name),
                propValue,
                Expression.Constant(member.Type)
            );

            blocks.Add(callWrite);
        }

        return Expression.Lambda<Action<object, DbCmd>>(
            Expression.Block([typedEntity], blocks),
            entityParam, cmdParam
        ).Compile();
    }
}

public static class DbValueMapper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasColumn(DbDataReader reader, string name)
        => reader.HasColumn(name);
    
    // Read: DB -> C# Value
    public static object? ReadValue(DbDataReader reader, string name, Type targetType)
    {
        var raw = reader[name];
        if (raw == DBNull.Value) return null;
        
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (type.IsEnum)
        {
            if (raw is int or long or byte or short)
                return Enum.ToObject(type, raw);
            
            return raw is string s 
                ? Enum.Parse(type, s, ignoreCase: true)
                : Enum.ToObject(type, raw);
        }

        if (type == typeof(bool))
            return ToBool(raw);
        
        return type.IsInstanceOfType(raw) 
            ? raw // 타입이 맞으면 바로 변환
            : Convert.ChangeType(raw, type); 
    }

    // Write: C# Value -> DB Param
    public static void WriteValue(DbCmd cmd, string name, object val, Type type)
    {
        var spec = DbParamUtils.ResolveDbParamSpec(type, val);
        cmd.Add(name, spec.DbType, spec.Value, spec.Size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ToBool(object raw) => raw switch
    {
        bool b => b,
        IConvertible => Convert.ToInt64(raw) != 0,
        _ => Convert.ToBoolean(raw)
    };
}
