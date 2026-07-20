using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using SP.Core.Accessor;

namespace SP.Database;

public record EntityMetadata(
    Func<object> Create,
    Action<object, DbDataReader, int[]> Read,
    string[] ColumnNames,
    Action<object, DbExecutor> Write);

public static class DbEntityBinder
{
    private static readonly ConcurrentDictionary<Type, EntityMetadata> Cache = new();

    public static EntityMetadata Get(Type type)
        => Cache.GetOrAdd(type, Compile);

    private static EntityMetadata Compile(Type type)
    {
        var createFunc = Expression.Lambda<Func<object>>(Expression.New(type)).Compile();
        var (readAction, columnNames) = CompileRead(type);
        var writeAction = CompileWrite(type);
        return new EntityMetadata(createFunc, readAction, columnNames, writeAction);
    }

    private static (Action<object, DbDataReader, int[]>, string[]) CompileRead(Type type)
    {
        var entityParam = Expression.Parameter(typeof(object), "entity");
        var readerParam = Expression.Parameter(typeof(DbDataReader), "reader");
        var ordinalsParam = Expression.Parameter(typeof(int[]), "ordinals");

        // 로컬 변수: ((Type)entity)
        var typedEntity = Expression.Variable(type, "target");
        var assignEntity = Expression.Assign(typedEntity, Expression.Convert(entityParam, type));

        var blocks = new List<Expression> { assignEntity };
        var columnNames = new List<string>();
        var readMethod = typeof(DbValueMapper).GetMethod(nameof(DbValueMapper.ReadValueByOrdinal));

        var accessor = RuntimeTypeAccessor.GetOrCreate(type);
        foreach (var member in accessor.Members)
        {
            if (!member.CanSet || member.IgnoreSet) continue;

            // int ordinal = reader.GetOrdinal(name);
            // if (ordinal >= 0) { target.Prob = (Type)ReadValueByOrdinal(reader, ordinal, type); }
            
            columnNames.Add(member.Name);
            var arrayIndex = columnNames.Count - 1;

            var ordinalVar = Expression.Variable(typeof(int), $"ord_{member.Name}");

            // ordinal = ordinalsParam[arrayIndex];
            var assignOrdinal = Expression.Assign(
                ordinalVar,
                Expression.ArrayIndex(ordinalsParam, Expression.Constant(arrayIndex))
            );
            blocks.Add(assignOrdinal);

            // ReadValueByOrdinal(reader, ordinal, type)
            var callRead = Expression.Call(readMethod!,
                readerParam,
                ordinalVar,
                Expression.Constant(member.Type)
            );

            var assignProb = Expression.Assign(
                Expression.MakeMemberAccess(typedEntity, member.Info),
                Expression.Convert(callRead, member.Type)
            );

            // if (ordinal >= 0) { assignProb; }
            var checkAndAssign = Expression.IfThen(
                Expression.GreaterThanOrEqual(ordinalVar, Expression.Constant(0)),
                assignProb
            );

            blocks.Add(checkAndAssign);
        }

        var allVariables = new List<ParameterExpression> { typedEntity };
        allVariables.AddRange(blocks.OfType<BinaryExpression>().Where(b => b.Left is ParameterExpression)
            .Select(b => (ParameterExpression)b.Left));

        var lambda = Expression.Lambda<Action<object, DbDataReader, int[]>>(
            Expression.Block(allVariables.Distinct(), blocks),
            entityParam, readerParam, ordinalsParam
        ).Compile();

        return (lambda, columnNames.ToArray());
    }

    private static Action<object, DbExecutor> CompileWrite(Type type)
    {
        var entityParam = Expression.Parameter(typeof(object), "entity");
        var cmdParam = Expression.Parameter(typeof(DbExecutor), "cmd");

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

            var colName = member.Name;

            // WriteValue 호출
            var callWrite = Expression.Call(writeMethod!,
                cmdParam,
                Expression.Constant(colName),
                propValue,
                Expression.Constant(member.Type)
            );

            blocks.Add(callWrite);
        }

        return Expression.Lambda<Action<object, DbExecutor>>(
            Expression.Block([typedEntity], blocks),
            entityParam, cmdParam
        ).Compile();
    }
}

public static class DbValueMapper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetOrdinal(DbDataReader reader, string name)
    {
        try
        {
            return reader.GetOrdinal(name);
        }
        catch (IndexOutOfRangeException)
        {
            return -1;
        }
    }

    // Read: DB -> C# Value
    public static object? ReadValueByOrdinal(DbDataReader reader, int ordinal, Type targetType)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var raw = reader.GetValue(ordinal);

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
    public static void WriteValue(DbExecutor executor, string name, object val, Type type)
    {
        var spec = DbParamUtils.ResolveDbParamSpec(type, val);
        executor.Add(name, spec.DbType, spec.Value, spec.Size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ToBool(object raw) => raw switch
    {
        bool b => b,
        IConvertible => Convert.ToInt64(raw) != 0,
        _ => Convert.ToBoolean(raw)
    };
}
