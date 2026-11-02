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
            // Primitive, DateTime, String, byte[], Enum
            if (TryGetBuiltin(type, $"this.{memberName}", out var builtinWrite, out var builtinRead, out var builtinReset))
            {
                writeStatement = builtinWrite;
                readStatement = $"this.{memberName} = {builtinRead};";
                resetStatement = $"this.{memberName} = {builtinReset};";
                return true;
            }

            // T[] (1D array)
            if (type is IArrayTypeSymbol { Rank: 1 } arrayType)
            {
                return TryBuildArray(memberName, arrayType.ElementType, out writeStatement, out readStatement, out resetStatement);
            }

            // List<T> and Dictionary<TKey, TValue>
            if (type is INamedTypeSymbol { IsGenericType: true } namedType)
            {
                switch (namedType.ConstructedFrom.ToDisplayString())
                {
                    case KnownSymbols.ListGenericDef when namedType.TypeArguments.Length == 1:
                        return TryBuildList(memberName, namedType.TypeArguments[0], out writeStatement, out readStatement, out resetStatement);

                    case KnownSymbols.DictionaryGenericDef when namedType.TypeArguments.Length == 2:
                        return TryBuildDictionary(memberName, namedType.TypeArguments[0], namedType.TypeArguments[1], out writeStatement, out readStatement, out resetStatement);
                }
            }

            // Custom class implementing INetSerializable
            if (ImplementsINetSerializable(type))
            {
                return TryBuildCustomClass(memberName, type, out writeStatement, out readStatement, out resetStatement);
            }

            writeStatement = readStatement = resetStatement = string.Empty;
            return false;
        }

        private static bool TryBuildCustomClass(
            string memberName,
            ITypeSymbol typeSymbol,
            out string writeStatement,
            out string readStatement,
            out string resetStatement)
        {
            writeStatement = BuildWrite();
            readStatement = BuildRead();
            resetStatement = BuildReset();
            return true;

            string BuildWrite()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"if (this.{memberName} == null)");
                sb.AppendLine("    writer.WriteBool(false);");
                sb.AppendLine("else");
                sb.AppendLine("{");
                sb.AppendLine("    writer.WriteBool(true);");
                sb.AppendLine($"    this.{memberName}.Serialize(ref writer);");
                sb.AppendLine("}");
                return sb.ToString();
            }

            string BuildRead()
            {
                var fullTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                var sb = new StringBuilder();
                sb.AppendLine("if (!reader.ReadBool())");
                sb.AppendLine($"    this.{memberName} = null;");
                sb.AppendLine("else");
                sb.AppendLine("{");
                sb.AppendLine($"    if (this.{memberName} == null)");
                sb.AppendLine($"        this.{memberName} = new {fullTypeName}();");
                sb.AppendLine($"    this.{memberName}.Deserialize(ref reader);");
                sb.AppendLine("}");
                return sb.ToString();
            }

            string BuildReset()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"this.{memberName} = null;");
                return sb.ToString();
            }
        }

        private static bool TryBuildArray(
            string memberName,
            ITypeSymbol elemType,
            out string writeStatement,
            out string readStatement,
            out string resetStatement)
        {
            if (!TryBuildElement($"this.{memberName}[i]", elemType, "arr[i] = {0};", out var elemWrite, out var elemRead))
            {
                writeStatement = readStatement = resetStatement = string.Empty;
                return false;
            }

            writeStatement = BuildWrite();
            readStatement = BuildRead();
            resetStatement = BuildReset();
            return true;

            string BuildReset()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"this.{memberName} = null;");
                return sb.ToString();
            }

            string BuildWrite()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"if (this.{memberName} == null)");
                sb.AppendLine("    writer.WriteBool(false);");
                sb.AppendLine("else");
                sb.AppendLine("{");
                sb.AppendLine("    writer.WriteBool(true);");
                sb.AppendLine($"    writer.WriteVarUInt((uint)this.{memberName}.Length);");
                sb.AppendLine($"    for (var i = 0; i < this.{memberName}.Length; i++)");
                sb.AppendLine("    {");
                AppendIndented(sb, elemWrite, "        ");
                sb.AppendLine("    }");
                sb.AppendLine("}");
                return sb.ToString();
            }

            string BuildRead()
            {
                var elemFullName = elemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var arrayAlloc = elemType is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte }
                    ? "new byte[count][]"
                    : $"new {elemFullName}[count]";

                var sb = new StringBuilder();
                sb.AppendLine("if (!reader.ReadBool())");
                sb.AppendLine($"    this.{memberName} = null;");
                sb.AppendLine("else");
                sb.AppendLine("{");
                sb.AppendLine("    var count = (int)reader.ReadVarUInt();");
                sb.AppendLine($"    var arr = {arrayAlloc};");
                sb.AppendLine("    for (var i = 0; i < count; i++)");
                sb.AppendLine("    {");
                AppendIndented(sb, elemRead, "        ");
                sb.AppendLine("    }");
                sb.AppendLine($"    this.{memberName} = arr;");
                sb.AppendLine("}");
                return sb.ToString();
            }
        }

        private static bool TryBuildList(
            string memberName,
            ITypeSymbol elemType,
            out string writeStatement,
            out string readStatement,
            out string resetStatement)
        {
            if (!TryBuildElement($"this.{memberName}[i]", elemType, "list.Add({0});", out var elemWrite, out var elemRead))
            {
                writeStatement = readStatement = resetStatement = string.Empty;
                return false;
            }

            writeStatement = BuildWrite();
            readStatement = BuildRead();
            resetStatement = BuildReset();
            return true;

            string BuildWrite()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"if (this.{memberName} == null)");
                sb.AppendLine("    writer.WriteBool(false);");
                sb.AppendLine("else");
                sb.AppendLine("{");
                sb.AppendLine("    writer.WriteBool(true);");
                sb.AppendLine($"    writer.WriteVarUInt((uint)this.{memberName}.Count);");
                sb.AppendLine($"    for (var i = 0; i < this.{memberName}.Count; i++)");
                sb.AppendLine("    {");
                AppendIndented(sb, elemWrite, "        ");
                sb.AppendLine("    }");
                sb.AppendLine("}");
                return sb.ToString();
            }

            string BuildRead()
            {
                var elemFullName = elemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                var sb = new StringBuilder();
                sb.AppendLine("if (!reader.ReadBool())");
                sb.AppendLine($"    this.{memberName} = null;");
                sb.AppendLine("else");
                sb.AppendLine("{");
                sb.AppendLine("    var count = (int)reader.ReadVarUInt();");
                sb.AppendLine($"    var list = this.{memberName};");
                sb.AppendLine("    if (list == null)");
                sb.AppendLine("    {");
                sb.AppendLine($"        list = new global::System.Collections.Generic.List<{elemFullName}>(count);");
                sb.AppendLine($"        this.{memberName} = list;");
                sb.AppendLine("    }");
                sb.AppendLine("    else");
                sb.AppendLine("    {");
                sb.AppendLine("        list.Clear();");
                sb.AppendLine("        if (list.Capacity < count)");
                sb.AppendLine("            list.Capacity = count;");
                sb.AppendLine("    }");
                sb.AppendLine("    for (var i = 0; i < count; i++)");
                sb.AppendLine("    {");
                AppendIndented(sb, elemRead, "        ");
                sb.AppendLine("    }");
                sb.AppendLine("}");
                return sb.ToString();
            }

            string BuildReset()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"if (this.{memberName} != null)");
                sb.AppendLine($"    this.{memberName}.Clear();");
                return sb.ToString();
            }
        }

        private static bool TryBuildDictionary(
            string memberName,
            ITypeSymbol keyType,
            ITypeSymbol valType,
            out string writeStatement,
            out string readStatement,
            out string resetStatement)
        {
            if (!TryGetBuiltin(keyType, "kvp.Key", out var keyWrite, out var keyReadExpr, out _) ||
                !TryBuildElement("kvp.Value", valType, "dict[key] = {0};", out var valWrite, out var valRead))
            {
                writeStatement = readStatement = resetStatement = string.Empty;
                return false;
            }

            writeStatement = BuildWrite();
            readStatement = BuildRead();
            resetStatement = BuildReset();
            return true;

            string BuildWrite()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"if (this.{memberName} == null)");
                sb.AppendLine("    writer.WriteBool(false);");
                sb.AppendLine("else");
                sb.AppendLine("{");
                sb.AppendLine("    writer.WriteBool(true);");
                sb.AppendLine($"    writer.WriteVarUInt((uint)this.{memberName}.Count);");
                sb.AppendLine($"    foreach (var kvp in this.{memberName})");
                sb.AppendLine("    {");
                sb.AppendLine($"        {keyWrite}");
                AppendIndented(sb, valWrite, "        ");
                sb.AppendLine("    }");
                sb.AppendLine("}");
                return sb.ToString();
            }

            string BuildRead()
            {
                var keyFullName = keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var valFullName = valType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                var sb = new StringBuilder();
                sb.AppendLine("if (!reader.ReadBool())");
                sb.AppendLine($"    this.{memberName} = null;");
                sb.AppendLine("else");
                sb.AppendLine("{");
                sb.AppendLine("    var count = (int)reader.ReadVarUInt();");
                sb.AppendLine($"    var dict = this.{memberName};");
                sb.AppendLine("    if (dict == null)");
                sb.AppendLine("    {");
                sb.AppendLine($"        dict = new global::System.Collections.Generic.Dictionary<{keyFullName}, {valFullName}>(count);");
                sb.AppendLine($"        this.{memberName} = dict;");
                sb.AppendLine("    }");
                sb.AppendLine("    else");
                sb.AppendLine("    {");
                sb.AppendLine("        dict.Clear();");
                sb.AppendLine("    }");
                sb.AppendLine("    for (var i = 0; i < count; i++)");
                sb.AppendLine("    {");
                sb.AppendLine($"        var key = {keyReadExpr};");
                AppendIndented(sb, valRead, "        ");
                sb.AppendLine("    }");
                sb.AppendLine("}");
                return sb.ToString();
            }

            string BuildReset()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"if (this.{memberName} != null)");
                sb.AppendLine($"    this.{memberName}.Clear();");
                return sb.ToString();
            }
        }

        private static bool TryBuildElement(
            string valueExpr,
            ITypeSymbol elemType,
            string assignFormat,
            out string writeStmt,
            out string readStmt)
        {
            if (TryGetBuiltin(elemType, valueExpr, out writeStmt, out var readExpr, out _))
            {
                readStmt = string.Format(assignFormat, readExpr);
                return true;
            }

            if (ImplementsINetSerializable(elemType))
            {
                var elemFullName = elemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                writeStmt = BuildWrite();
                readStmt = BuildRead();
                return true;

                string BuildRead()
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"{elemFullName} item = null;");
                    sb.AppendLine("if (reader.ReadBool())");
                    sb.AppendLine("{");
                    sb.AppendLine($"    item = new {elemFullName}();");
                    sb.AppendLine("    item.Deserialize(ref reader);");
                    sb.AppendLine("}");
                    sb.AppendLine(string.Format(assignFormat, "item"));
                    return sb.ToString();
                }

                string BuildWrite()
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"var item = {valueExpr};");
                    sb.AppendLine("if (item == null)");
                    sb.AppendLine("    writer.WriteBool(false);");
                    sb.AppendLine("else");
                    sb.AppendLine("{");
                    sb.AppendLine("    writer.WriteBool(true);");
                    sb.AppendLine("    item.Serialize(ref writer);");
                    sb.AppendLine("}");
                    return sb.ToString();
                }
            }

            writeStmt = readStmt = string.Empty;
            return false;
        }

        internal static void AppendIndented(StringBuilder sb, string statement, string indent)
        {
            using var reader = new System.IO.StringReader(statement);
            while (reader.ReadLine() is { } line)
            {
                sb.Append(indent).AppendLine(line);
            }
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

        private static bool ImplementsINetSerializable(ITypeSymbol type)
        {
            return type is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } namedType &&
                   namedType.AllInterfaces.Any(static i => i.ToDisplayString() == KnownSymbols.INetSerializable);
        }
    }
}
