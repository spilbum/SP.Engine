using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SP.Engine.SourceGenerator
{
    internal static class MemberCodeBuilder
    {
        private enum ElementKind
        {
            Unsupported,
            ValueOrBuiltin,
            CustomData
        }

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

        public static bool TryCreateStatements(
            string memberName,
            ITypeSymbol type,
            out string writeStatement,
            out string readStatement,
            out string resetStatement)
        {
            var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // 1. Primitive types
            if (PrimitiveMethods.TryGetValue(type.SpecialType, out var methods))
            {
                writeStatement = $"writer.{methods.WriteMethod}(this.{memberName});";
                readStatement = $"this.{memberName} = reader.{methods.ReadMethod}();";
                resetStatement = $"this.{memberName} = default({fullTypeName});";
                return true;
            }

            // 2. DateTime
            if (type.SpecialType == SpecialType.System_DateTime || type.ToDisplayString() == KnownSymbols.DateTimeType)
            {
                writeStatement = $"writer.WriteDateTime(this.{memberName});";
                readStatement = $"this.{memberName} = reader.ReadDateTime();";
                resetStatement = $"this.{memberName} = default({fullTypeName});";
                return true;
            }

            // 3. String
            if (type.SpecialType == SpecialType.System_String)
            {
                writeStatement = $"writer.WriteString(this.{memberName});";
                readStatement = $"this.{memberName} = reader.ReadString();";
                resetStatement = $"this.{memberName} = null;";
                return true;
            }

            // 4. byte[]
            if (type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte })
            {
                writeStatement = $"writer.WriteByteArray(this.{memberName});";
                readStatement = $"this.{memberName} = reader.ReadByteArray();";
                resetStatement = $"this.{memberName} = null;";
                return true;
            }

            // 5. Enum
            if (type.TypeKind == TypeKind.Enum)
            {
                writeStatement = $"writer.WriteEnum(this.{memberName});";
                readStatement = $"this.{memberName} = reader.ReadEnum<{fullTypeName}>();";
                resetStatement = $"this.{memberName} = default({fullTypeName});";
                return true;
            }

            // 6. T[] (1D array of supported element type)
            if (type is IArrayTypeSymbol { Rank: 1 } arrayType)
            {
                var elemType = arrayType.ElementType;
                var elemKind = ClassifyElementType(elemType);
                if (elemKind == ElementKind.Unsupported)
                {
                    writeStatement = readStatement = resetStatement = string.Empty;
                    return false;
                }

                var elemFullName = elemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var arrayReadMethod = elemKind == ElementKind.CustomData ? "ReadDataArray" : "ReadArray";

                writeStatement = $"writer.WriteArray(this.{memberName});";
                readStatement = $"this.{memberName} = reader.{arrayReadMethod}<{elemFullName}>();";
                resetStatement = $"this.{memberName} = null;";
                return true;
            }

            // 7. List<T> and Dictionary<TKey, TValue>
            if (type is INamedTypeSymbol { IsGenericType: true } namedType)
            {
                var genericDef = namedType.ConstructedFrom.ToDisplayString();
                switch (genericDef)
                {
                    case KnownSymbols.ListGenericDef when namedType.TypeArguments.Length == 1:
                    {
                        var elemType = namedType.TypeArguments[0];
                        var elemKind = ClassifyElementType(elemType);
                        if (elemKind == ElementKind.Unsupported)
                        {
                            writeStatement = readStatement = resetStatement = string.Empty;
                            return false;
                        }

                        var elemFullName = elemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var listReadMethod = elemKind == ElementKind.CustomData ? "ReadDataList" : "ReadList";

                        writeStatement = $"writer.WriteList(this.{memberName});";
                        readStatement = $"this.{memberName} = reader.{listReadMethod}<{elemFullName}>(this.{memberName});";
                        resetStatement = $"if (this.{memberName} != null) this.{memberName}.Clear();";
                        return true;
                    }
                    case KnownSymbols.DictionaryGenericDef when namedType.TypeArguments.Length == 2:
                    {
                        var keyType = namedType.TypeArguments[0];
                        var valType = namedType.TypeArguments[1];
                        var keyKind = ClassifyElementType(keyType);
                        var valKind = ClassifyElementType(valType);
                        if (keyKind != ElementKind.ValueOrBuiltin || valKind == ElementKind.Unsupported)
                        {
                            writeStatement = readStatement = resetStatement = string.Empty;
                            return false;
                        }

                        var keyFullName = keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var valFullName = valType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var dictReadMethod = valKind == ElementKind.CustomData ? "ReadDataDict" : "ReadDict";

                        writeStatement = $"writer.WriteDict(this.{memberName});";
                        readStatement = $"this.{memberName} = reader.{dictReadMethod}<{keyFullName}, {valFullName}>(this.{memberName});";
                        resetStatement = $"if (this.{memberName} != null) this.{memberName}.Clear();";
                        return true;
                    }
                }
            }

            // 8. Custom Sub-class 
            if (IsCustomSerializableClass(type))
            {
                writeStatement = $"writer.WriteData(this.{memberName});";
                readStatement = $"this.{memberName} = reader.ReadData<{fullTypeName}>();";
                resetStatement = $"this.{memberName} = null;";
                return true;
            }

            writeStatement = readStatement = resetStatement = string.Empty;
            return false;
        }

        private static ElementKind ClassifyElementType(ITypeSymbol type)
        {
            if (PrimitiveMethods.ContainsKey(type.SpecialType) ||
                type.SpecialType == SpecialType.System_DateTime ||
                type.ToDisplayString() == KnownSymbols.DateTimeType ||
                type.SpecialType == SpecialType.System_String ||
                type.TypeKind == TypeKind.Enum)
            {
                return ElementKind.ValueOrBuiltin;
            }

            if (type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte })
            {
                return ElementKind.ValueOrBuiltin;
            }

            return IsCustomSerializableClass(type) ? ElementKind.CustomData : ElementKind.Unsupported;
        }

        private static bool IsCustomSerializableClass(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class, SpecialType: SpecialType.None } namedType)
                return false;

            if (namedType.IsAbstract || namedType.IsGenericType || namedType.ContainingType != null)
                return false;

            return namedType.GetAttributes().Any(static attr =>
            {
                var attrName = attr.AttributeClass?.ToDisplayString();
                return attrName is KnownSymbols.NetSerializableAttribute;
            }) || namedType.AllInterfaces.Any(static i => i.ToDisplayString() == KnownSymbols.INetSerializable);
        }
    }
}
