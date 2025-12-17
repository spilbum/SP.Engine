using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SP.Shared.Resource.Table;

namespace SP.Shared.Resource.CodeGen;

public static class ReferenceCodeGenerator
{
    public static void Generate(
        List<RefTableSchema> schemas,
        string outputDir,
        string @namespace)
    {
        Directory.CreateDirectory(outputDir);

        foreach (var schema in schemas)
        {
            var className = GetClassName(schema);
            var filePath = Path.Combine(outputDir, $"{className}.cs");
            
            var code = GenerateCSharpCode(schema, @namespace);
            File.WriteAllText(filePath, code, Encoding.UTF8);
        }
    }

    private static string GenerateCSharpCode(
        RefTableSchema schema, 
        string @namespace)
    {
        var cw = new CodeWriter();
        var className = GetClassName(schema);
        var tableName = schema.Name;

        var keyColumn = schema.Columns.FirstOrDefault(c => c.IsKey);
        if (keyColumn == null)
            throw new InvalidOperationException($"Table '{schema.Name}' must have at least one key.");
        
        var keyTypeName = GetCSharpType(keyColumn.Type);
        var keyPropName = ToIdentifier(keyColumn.Name);
        var dicName = GetDictionaryName(schema);
        var buildMethodName = GetBuildMethodName(schema);
        
        cw.WriteLine("using SP.Shared.Resource;");
        cw.WriteLine();
        
        using (cw.Block($"namespace {@namespace}"))
        {
            using (cw.Block($"public sealed class {className}"))
            {
                foreach (var column in schema.Columns)
                {
                    var typeName = GetCSharpType(column.Type);
                    var propName = ToIdentifier(column.Name);
                    cw.WriteLine(typeName == "string"
                        ? $"public string {propName} {{ get; set; }} = string.Empty;"
                        : $"public {typeName} {propName} {{ get; set; }}");
                }
                
                cw.WriteLine();

                using (cw.Block($"public static {className} Create(RefTableSchema schema, RefRow row)"))
                {
                    foreach (var column in schema.Columns)
                    {
                        var propName = ToIdentifier(column.Name);
                        cw.WriteLine($"var idx{propName} = schema.GetColumnIndex(\"{column.Name}\");");
                    }

                    cw.WriteLine();
                    
                    using (cw.Block($"return new {className}", "};"))
                    {
                        for (var i = 0; i < schema.Columns.Count; i++)
                        {
                            var column = schema.Columns[i];
                            var propName = ToIdentifier(column.Name);
                            var typeName = GetCSharpType(column.Type);
                            var suffix = i == schema.Columns.Count - 1 ? "" : ",";
                            cw.WriteLine($"{propName} = row.Get<{typeName}>(idx{propName}){suffix}");
                        }
                    }
                }
            }
            
            cw.WriteLine();
        
            using (cw.Block($"public sealed partial class ReferenceTableManager"))
            {
                cw.WriteLine($"public Dictionary<{keyTypeName}, {className}> {dicName} {{ get; private set; }} = new();");
                cw.WriteLine();

                using (cw.Block($"private void {buildMethodName}()"))
                {
                    cw.WriteLine($"const string tableName = \"{tableName}\";");
                    using (cw.Block("if (!TryGet(tableName, out var schema, out var table))"))
                    {
                        cw.WriteLine("return;");
                    }
                    
                    cw.WriteLine();
                    cw.WriteLine($"{dicName}.Clear();");
                    cw.WriteLine();
                    
                    cw.WriteLine($"var dict = new Dictionary<{keyTypeName}, {className}>(table.Rows.Count);");
                    using (cw.Block("foreach (var row in table.Rows)"))
                    {
                        cw.WriteLine($"var obj = {className}.Create(schema, row);");
                        cw.WriteLine($"dict[obj.{keyPropName}] = obj;");
                    }
                    
                    cw.WriteLine($"{dicName} = dict;");
                }
            
                cw.WriteLine();

                using (cw.Block($"public {className}? Get{className}({keyTypeName} id)"))
                {
                    cw.WriteLine($"return {dicName}.TryGetValue(id, out var v) ? v : null;");
                }
            }
        }
        
        return cw.ToString();
    }

    private static string GetClassName(RefTableSchema schema)
        => "Ref" + ToPascal(schema.Name);

    private static string GetDictionaryName(RefTableSchema schema)
    {
        var n = ToPascal(schema.Name);
        return n.EndsWith("s") ? n : n + "s";
    }
    
    private static string GetBuildMethodName(RefTableSchema schema)
        => "Build" + GetClassName(schema);
    
    private static string GetCSharpType(ColumnType type)
    {
        return type switch
        {
            ColumnType.String => "string",
            ColumnType.Byte => "byte",
            ColumnType.Int32 => "int",
            ColumnType.Int64 => "long",
            ColumnType.Float => "float",
            ColumnType.Double => "double",
            ColumnType.Boolean => "bool",
            ColumnType.DateTime => "DateTime",
            _ => "object"
        };
    }

    private static string ToPascal(string name)
    {
        var parts = name
            .Split(['_', ' ', '-'], StringSplitOptions.RemoveEmptyEntries);

        return string.Concat(
            parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static string ToIdentifier(string name)
    {
        var pascal = ToPascal(name);
        if (!string.IsNullOrEmpty(pascal) && char.IsDigit(pascal[0]))
            pascal = "_" + pascal;
        return pascal;
    }
}
