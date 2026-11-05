using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SP.Database;

public record EntityMetadata(
    Func<object> Create,
    Action<object, DbDataReader, int[]> Read,
    string[] ColumnNames,
    Action<object, DbExecutor> Write);

public static class DbEntityBinder
{
    private static readonly ConcurrentDictionary<Type, EntityMetadata> Cache = new();

    private readonly record struct DbMemberMeta(
        string ColumnName,
        Type MemberType,
        MemberInfo MemberInfo,
        bool CanSetFromDb,
        bool CanGetForDb);

    public static EntityMetadata Get(Type type)
        => Cache.GetOrAdd(type, Compile);

    private static EntityMetadata Compile(Type type)
    {
        var members = ExtractMembers(type);
        var createFunc = Expression.Lambda<Func<object>>(Expression.New(type)).Compile();
        var (readAction, columnNames) = CompileRead(type, members);
        var writeAction = CompileWrite(type, members);
        return new EntityMetadata(createFunc, readAction, columnNames, writeAction);
    }

    private static List<DbMemberMeta> ExtractMembers(Type type)
    {
        var members = new List<DbMemberMeta>();

        foreach (var member in type.GetMembers(BindingFlags.Instance | BindingFlags.Public))
        {
            switch (member)
            {
                case FieldInfo field:
                {
                    var attr = field.GetCustomAttribute<DbColumnAttribute>();
                    var columnName = attr?.Name ?? field.Name;
                    var canSetFromDb = field is { IsInitOnly: false, IsLiteral: false } && !(attr?.IgnoreRead ?? false);
                    var canGetForDb = !(attr?.IgnoreWrite ?? false);
                    members.Add(new DbMemberMeta(columnName, field.FieldType, field, canSetFromDb, canGetForDb));
                    break;
                }
                case PropertyInfo prop when prop.GetIndexParameters().Length == 0:
                {
                    var attr = prop.GetCustomAttribute<DbColumnAttribute>();
                    var columnName = attr?.Name ?? prop.Name;
                    var hasGetter = prop.GetMethod is { IsPublic: true, IsStatic: false };
                    var hasSetter = prop.SetMethod is { IsPublic: true, IsStatic: false };
                    var canSetFromDb = hasSetter && !(attr?.IgnoreRead ?? false);
                    var canGetForDb = hasGetter && !(attr?.IgnoreWrite ?? false);
                    members.Add(new DbMemberMeta(columnName, prop.PropertyType, prop, canSetFromDb, canGetForDb));
                    break;
                }
            }
        }

        return members;
    }

    private static (Action<object, DbDataReader, int[]>, string[]) CompileRead(Type type, List<DbMemberMeta> members)
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

        foreach (var member in members.Where(member => member.CanSetFromDb))
        {
            // int ordinal = reader.GetOrdinal(name);
            // if (ordinal >= 0) { target.Prop = (Type)ReadValueByOrdinal(reader, ordinal, type); }
            
            columnNames.Add(member.ColumnName);
            var arrayIndex = columnNames.Count - 1;

            var ordinalVar = Expression.Variable(typeof(int), $"ord_{member.ColumnName}");

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
                Expression.Constant(member.MemberType)
            );

            var assignProp = Expression.Assign(
                Expression.MakeMemberAccess(typedEntity, member.MemberInfo),
                Expression.Convert(callRead, member.MemberType)
            );

            // if (ordinal >= 0) { assignProp; }
            var checkAndAssign = Expression.IfThen(
                Expression.GreaterThanOrEqual(ordinalVar, Expression.Constant(0)),
                assignProp
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

    private static Action<object, DbExecutor> CompileWrite(Type type, List<DbMemberMeta> members)
    {
        var entityParam = Expression.Parameter(typeof(object), "entity");
        var cmdParam = Expression.Parameter(typeof(DbExecutor), "cmd");

        var typedEntity = Expression.Variable(type, "target");
        var assignEntity = Expression.Assign(typedEntity, Expression.Convert(entityParam, type));

        var blocks = new List<Expression> { assignEntity };

        var writeMethod = typeof(DbValueMapper).GetMethod(nameof(DbValueMapper.WriteValue));

        blocks.AddRange(
            from member in members 
            where member.CanGetForDb 
            let propValue = Expression.Convert(Expression.MakeMemberAccess(typedEntity, member.MemberInfo), typeof(object)) 
            select Expression.Call(writeMethod!, cmdParam, Expression.Constant(member.ColumnName), propValue, Expression.Constant(member.MemberType)
            ));

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
