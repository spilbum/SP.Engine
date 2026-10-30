using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace SP.Engine.SourceGenerator
{
    internal static class MemberCodeBuilder
    {
        private static readonly Dictionary<SpecialType, (string WriteMethod, string ReadMethod)> PrimitiveMethods = new()
        {
            { SpecialType.System_Boolean, ("WriteBool", "ReadBool") },
            { SpecialType.System_Byte, ("WriteByte", "ReadByte") },
            { SpecialType.System_SByte, ("WriteSByte", "ReadSByte") },
            { SpecialType.System_Int16, ("WriteInt16", "ReadInt16") },
            { SpecialType.System_UInt16, ("WriteUInt16", "ReadUInt16") },
            { SpecialType.System_Int32, ("WriteInt32", "ReadInt32") },
            { SpecialType.System_UInt32, ("WriteUInt32", "ReadUInt32") },
            { SpecialType.System_Int64, ("WriteInt64", "ReadInt64") },
            { SpecialType.System_UInt64, ("WriteUInt64", "ReadUInt64") },
            { SpecialType.System_Single, ("WriteSingle", "ReadSingle") },
            { SpecialType.System_Double, ("WriteDouble", "ReadDouble") }
        };

        public static IEnumerable<(string Name, ITypeSymbol Type, bool CanGet, bool CanSet)> GetSerializableMembers(INamedTypeSymbol typeSymbol)
        {
            var typeChain = new List<INamedTypeSymbol>();
            for (var current = typeSymbol; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                typeChain.Add(current);
            }
            typeChain.Reverse();

            foreach (var member in typeChain.SelectMany(static t => t.GetMembers()))
            {
                if (member.IsStatic || member.DeclaredAccessibility != Accessibility.Public)
                    continue;

                switch (member)
                {
                    case IFieldSymbol { IsImplicitlyDeclared: false } field:
                    {
                        var canSet = field is { IsReadOnly: false, IsConst: false };
                        yield return (field.Name, field.Type, true, canSet);
                        break;
                    }
                    case IPropertySymbol { IsIndexer: false } prop:
                    {
                        var canGet = prop.GetMethod is { DeclaredAccessibility: Accessibility.Public, IsStatic: false };
                        var canSet = prop.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, IsInitOnly: false };
                        if (canGet || canSet)
                            yield return (prop.Name, prop.Type, canGet, canSet);
                        break;
                    }
                }
            }
        }

        public static bool TryCreateStatements(
            string memberName,
            ITypeSymbol type,
            out string writeStatement,
            out string readStatement,
            out string resetStatement)
        {
            var visiting = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            return TryCreateStatementsCore(
                $"this.{memberName}",
                type,
                depth: 0,
                visiting,
                out writeStatement,
                out readStatement,
                out resetStatement);
        }

        private static bool TryCreateStatementsCore(
            string targetExpr,
            ITypeSymbol type,
            int depth,
            HashSet<ITypeSymbol> visiting,
            out string writeStatement,
            out string readStatement,
            out string resetStatement)
        {
            // 1. Primitive, DateTime, String, byte[], Enum
            if (TryGetBuiltin(type, targetExpr, out var builtinWrite, out var builtinRead, out var builtinReset))
            {
                writeStatement = builtinWrite;
                readStatement = $"{targetExpr} = {builtinRead};";
                resetStatement = $"{targetExpr} = {builtinReset};";
                return true;
            }

            // 2. T[] (1D array)
            if (type is IArrayTypeSymbol { Rank: 1 } arrayType)
            {
                return TryBuildArray(targetExpr, arrayType.ElementType, depth, visiting, out writeStatement, out readStatement, out resetStatement);
            }

            // 3. List<T> and Dictionary<TKey, TValue>
            if (type is INamedTypeSymbol { IsGenericType: true } namedType)
            {
                switch (namedType.ConstructedFrom.ToDisplayString())
                {
                    case KnownSymbols.ListGenericDef when namedType.TypeArguments.Length == 1:
                        return TryBuildList(targetExpr, namedType.TypeArguments[0], depth, visiting, out writeStatement, out readStatement, out resetStatement);

                    case KnownSymbols.DictionaryGenericDef when namedType.TypeArguments.Length == 2:
                        return TryBuildDictionary(targetExpr, namedType.TypeArguments[0], namedType.TypeArguments[1], depth, visiting, out writeStatement, out readStatement, out resetStatement);
                }
            }

            // 4. Custom Sub-class
            if (IsCandidateClass(type, visiting))
            {
                return TryBuildCustomClass(targetExpr, type, depth, visiting, out writeStatement, out readStatement, out resetStatement);
            }

            writeStatement = readStatement = resetStatement = string.Empty;
            return false;
        }

        private static bool TryBuildArray(
            string targetExpr,
            ITypeSymbol elemType,
            int depth,
            HashSet<ITypeSymbol> visiting,
            out string writeStatement,
            out string readStatement,
            out string resetStatement)
        {
            var idxVar = $"i{depth}";
            var cntVar = $"count{depth}";
            var arrVar = $"arr{depth}";

            if (!TryBuildElement($"{targetExpr}[{idxVar}]", elemType, $"{arrVar}[{idxVar}] = {{0}};", depth + 1, visiting, out var elemWrite, out var elemRead))
            {
                writeStatement = readStatement = resetStatement = string.Empty;
                return false;
            }

            var elemFullName = elemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var arrayAlloc = elemType is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte }
                ? $"new byte[{cntVar}][]"
                : $"new {elemFullName}[{cntVar}]";

            writeStatement =
                $"if ({targetExpr} == null) writer.WriteBool(false); " +
                $"else {{ writer.WriteBool(true); writer.WriteVarUInt((uint){targetExpr}.Length); " +
                $"for (var {idxVar} = 0; {idxVar} < {targetExpr}.Length; {idxVar}++) {{ {elemWrite} }} }}";

            readStatement =
                $"if (!reader.ReadBool()) {targetExpr} = null; " +
                $"else {{ var {cntVar} = (int)reader.ReadVarUInt(); var {arrVar} = {arrayAlloc}; " +
                $"for (var {idxVar} = 0; {idxVar} < {cntVar}; {idxVar}++) {{ {elemRead} }} {targetExpr} = {arrVar}; }}";

            resetStatement = $"{targetExpr} = null;";
            return true;
        }

        private static bool TryBuildList(
            string targetExpr,
            ITypeSymbol elemType,
            int depth,
            HashSet<ITypeSymbol> visiting,
            out string writeStatement,
            out string readStatement,
            out string resetStatement)
        {
            var idxVar = $"i{depth}";
            var cntVar = $"count{depth}";
            var listVar = $"list{depth}";

            if (!TryBuildElement($"{targetExpr}[{idxVar}]", elemType, $"{listVar}.Add({{0}});", depth + 1, visiting, out var elemWrite, out var elemRead))
            {
                writeStatement = readStatement = resetStatement = string.Empty;
                return false;
            }

            var elemFullName = elemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            writeStatement =
                $"if ({targetExpr} == null) writer.WriteBool(false); " +
                $"else {{ writer.WriteBool(true); writer.WriteVarUInt((uint){targetExpr}.Count); " +
                $"for (var {idxVar} = 0; {idxVar} < {targetExpr}.Count; {idxVar}++) {{ {elemWrite} }} }}";

            readStatement =
                $"if (!reader.ReadBool()) {targetExpr} = null; " +
                $"else {{ var {cntVar} = (int)reader.ReadVarUInt(); var {listVar} = {targetExpr}; " +
                $"if ({listVar} == null) {{ {listVar} = new global::System.Collections.Generic.List<{elemFullName}>({cntVar}); {targetExpr} = {listVar}; }} " +
                $"else {{ {listVar}.Clear(); if ({listVar}.Capacity < {cntVar}) {listVar}.Capacity = {cntVar}; }} " +
                $"for (var {idxVar} = 0; {idxVar} < {cntVar}; {idxVar}++) {{ {elemRead} }} }}";

            resetStatement = $"if ({targetExpr} != null) {targetExpr}.Clear();";
            return true;
        }

        private static bool TryBuildDictionary(
            string targetExpr,
            ITypeSymbol keyType,
            ITypeSymbol valType,
            int depth,
            HashSet<ITypeSymbol> visiting,
            out string writeStatement,
            out string readStatement,
            out string resetStatement)
        {
            var idxVar = $"i{depth}";
            var cntVar = $"count{depth}";
            var kvpVar = $"kvp{depth}";
            var dictVar = $"dict{depth}";
            var keyVar = $"key{depth}";

            if (!TryGetBuiltin(keyType, $"{kvpVar}.Key", out var keyWrite, out var keyReadExpr, out _) ||
                !TryBuildElement($"{kvpVar}.Value", valType, $"{dictVar}[{keyVar}] = {{0}};", depth + 1, visiting, out var valWrite, out var valRead))
            {
                writeStatement = readStatement = resetStatement = string.Empty;
                return false;
            }

            var keyFullName = keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var valFullName = valType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            writeStatement =
                $"if ({targetExpr} == null) writer.WriteBool(false); " +
                $"else {{ writer.WriteBool(true); writer.WriteVarUInt((uint){targetExpr}.Count); " +
                $"foreach (var {kvpVar} in {targetExpr}) {{ {keyWrite} {valWrite} }} }}";

            readStatement =
                $"if (!reader.ReadBool()) {targetExpr} = null; " +
                $"else {{ var {cntVar} = (int)reader.ReadVarUInt(); var {dictVar} = {targetExpr}; " +
                $"if ({dictVar} == null) {{ {dictVar} = new global::System.Collections.Generic.Dictionary<{keyFullName}, {valFullName}>({cntVar}); {targetExpr} = {dictVar}; }} " +
                $"else {{ {dictVar}.Clear(); }} " +
                $"for (var {idxVar} = 0; {idxVar} < {cntVar}; {idxVar}++) {{ var {keyVar} = {keyReadExpr}; {valRead} }} }}";

            resetStatement = $"if ({targetExpr} != null) {targetExpr}.Clear();";
            return true;
        }

        private static bool TryBuildCustomClass(
            string targetExpr,
            ITypeSymbol type,
            int depth,
            HashSet<ITypeSymbol> visiting,
            out string writeStatement,
            out string readStatement,
            out string resetStatement)
        {
            var objVar = $"obj{depth}";
            if (!TryBuildCustomClassBody(type, objVar, depth + 1, visiting, out var bodyWrite, out var bodyRead))
            {
                writeStatement = readStatement = resetStatement = string.Empty;
                return false;
            }

            var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            writeStatement = BuildNullableObjectWrite(objVar, targetExpr, bodyWrite);
            readStatement =
                $"if (!reader.ReadBool()) {targetExpr} = null; " +
                $"else {{ var {objVar} = {targetExpr}; if ({objVar} == null) {{ {objVar} = new {fullTypeName}(); {targetExpr} = {objVar}; }} {bodyRead} }}";
            resetStatement = $"{targetExpr} = null;";
            return true;
        }

        private static bool TryBuildElement(
            string valueExpr,
            ITypeSymbol elemType,
            string assignFormat,
            int depth,
            HashSet<ITypeSymbol> visiting,
            out string writeStmt,
            out string readStmt)
        {
            if (TryGetBuiltin(elemType, valueExpr, out writeStmt, out var readExpr, out _))
            {
                readStmt = string.Format(assignFormat, readExpr);
                return true;
            }

            var itemVar = $"item{depth}";
            if (IsCandidateClass(elemType, visiting) &&
                TryBuildCustomClassBody(elemType, itemVar, depth + 1, visiting, out var bodyWrite, out var bodyRead))
            {
                var elemFullName = elemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                writeStmt = BuildNullableObjectWrite(itemVar, valueExpr, bodyWrite);
                readStmt = $"{elemFullName} {itemVar} = null; if (reader.ReadBool()) {{ {itemVar} = new {elemFullName}(); {bodyRead} }} {string.Format(assignFormat, itemVar)}";
                return true;
            }

            writeStmt = readStmt = string.Empty;
            return false;
        }

        private static string BuildNullableObjectWrite(string objVar, string valueExpr, string bodyWrite)
        {
            return $"var {objVar} = {valueExpr}; if ({objVar} == null) writer.WriteBool(false); else {{ writer.WriteBool(true); {bodyWrite} }}";
        }

        private static bool TryGetBuiltin(
            ITypeSymbol type,
            string valueExpr,
            out string writeStatement,
            out string readExpr,
            out string resetExpr)
        {
            var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (PrimitiveMethods.TryGetValue(type.SpecialType, out var methods))
            {
                writeStatement = $"writer.{methods.WriteMethod}({valueExpr});";
                readExpr = $"reader.{methods.ReadMethod}()";
                resetExpr = $"default({fullTypeName})";
                return true;
            }

            if (type.SpecialType == SpecialType.System_DateTime || type.ToDisplayString() == KnownSymbols.DateTimeType)
            {
                writeStatement = $"writer.WriteDateTime({valueExpr});";
                readExpr = "reader.ReadDateTime()";
                resetExpr = $"default({fullTypeName})";
                return true;
            }

            if (type.SpecialType == SpecialType.System_String)
            {
                writeStatement = $"writer.WriteString({valueExpr});";
                readExpr = "reader.ReadString()";
                resetExpr = "null";
                return true;
            }

            if (type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte })
            {
                writeStatement = $"writer.WriteByteArray({valueExpr});";
                readExpr = "reader.ReadByteArray()";
                resetExpr = "null";
                return true;
            }

            if (type.TypeKind == TypeKind.Enum)
            {
                writeStatement = $"writer.WriteEnum({valueExpr});";
                readExpr = $"reader.ReadEnum<{fullTypeName}>()";
                resetExpr = $"default({fullTypeName})";
                return true;
            }

            writeStatement = readExpr = resetExpr = string.Empty;
            return false;
        }

        private static bool TryBuildCustomClassBody(
            ITypeSymbol type,
            string instanceVar,
            int depth,
            HashSet<ITypeSymbol> visiting,
            out string writeBody,
            out string readBody)
        {
            if (type is not INamedTypeSymbol namedType || !visiting.Add(namedType))
            {
                writeBody = readBody = string.Empty;
                return false;
            }

            try
            {
                var writeSb = new StringBuilder();
                var readSb = new StringBuilder();

                foreach (var (name, memberType, canGet, canSet) in GetSerializableMembers(namedType))
                {
                    if (!TryCreateStatementsCore($"{instanceVar}.{name}", memberType, depth, visiting, out var mWrite, out var mRead, out _))
                    {
                        writeBody = readBody = string.Empty;
                        return false;
                    }

                    if (canGet)
                    {
                        if (writeSb.Length > 0) writeSb.Append(' ');
                        writeSb.Append(mWrite);
                    }

                    if (canSet)
                    {
                        if (readSb.Length > 0) readSb.Append(' ');
                        readSb.Append(mRead);
                    }
                }

                writeBody = writeSb.ToString();
                readBody = readSb.ToString();
                return true;
            }
            finally
            {
                visiting.Remove(namedType);
            }
        }

        private static bool IsCandidateClass(ITypeSymbol type, HashSet<ITypeSymbol> visiting)
        {
            if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class, SpecialType: SpecialType.None, IsAbstract: false, IsGenericType: false } namedType)
                return false;

            if (visiting.Contains(namedType))
                return false;

            for (var checkType = namedType; checkType != null; checkType = checkType.ContainingType)
            {
                if (checkType.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
                    return false;
            }

            return namedType.InstanceConstructors.Length == 0 ||
                   namedType.InstanceConstructors.Any(static c =>
                       !c.IsStatic &&
                       c.Parameters.Length == 0 &&
                       c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal);
        }
    }
}
