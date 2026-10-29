using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SP.Engine.SourceGenerator
{
    [Generator(LanguageNames.CSharp)]
    public sealed class ProtocolDataGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // [ProtocolData] 
            var protocolTypeProvider = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    KnownSymbols.ProtocolDataAttribute,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (context, ct) => TransformProtocolTypeModel(context, ct))
                .Where(static bundle => bundle != null);

            context.RegisterSourceOutput(protocolTypeProvider, static (spc, protocol) =>
            {
                if (protocol == null) return;
                GenerateProtocolType(spc, protocol);
            });
            
            // [NetSerializable] 
            var complexTypeProvider = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    KnownSymbols.NetSerializableAttribute,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (context, ct) => TransformComplexTypeModel(context, ct))
                .Where(static s => s != null);
            
            context.RegisterSourceOutput(complexTypeProvider, static (spc, subType) =>
            {
                if (subType == null) return;
                GenerateComplexType(spc, subType);
            });

            var registryPolicies = protocolTypeProvider
                .Select(static (p, _) => new ProtocolPolicyModel(p!.AssemblyName, p.Id, p.Encrypt, p.Compress))
                .Collect();
            
            context.RegisterSourceOutput(registryPolicies, static (spc, entries) =>
            {
                if (entries.IsDefaultOrEmpty) return;
                
                var seenIds = new HashSet<ushort>();
                var uniquePolicies = new List<ProtocolPolicyModel>(entries.Length);

                foreach (var entry in Enumerable.OfType<ProtocolPolicyModel>(entries))
                {
                    if (!seenIds.Add(entry.Id))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.DuplicateProtocolId,
                            Location.None,
                            entry.Id,
                            entry.AssemblyName));
                        continue;
                    }
                    
                    uniquePolicies.Add(entry);
                }
                
                if (uniquePolicies.Count == 0) return;
                
                var source = BuildProtocolPolicyRegistrySource(uniquePolicies);
                spc.AddSource("ProtocolPolicyRegistry.g.cs", SourceText.From(source, Encoding.UTF8));
            });
        }

        private static ProtocolTypeModel? TransformProtocolTypeModel(GeneratorAttributeSyntaxContext context, CancellationToken ct)
        {
            if (context.TargetSymbol is not INamedTypeSymbol typeSymbol || typeSymbol.IsAbstract || typeSymbol.IsGenericType)
                return null;
            
            if (typeSymbol.ContainingAssembly == null)
                return null;

            var attr = context.Attributes.FirstOrDefault();
            if (attr == null)
                return null;

            // 생성자 디폴트 매개번수 추출
            var ctor = attr.AttributeConstructor;
            var channel = 0;
            var encrypt = 0;
            var compress = 0;

            if (ctor != null)
            {
                foreach (var param in ctor.Parameters)
                {
                    if (!param.HasExplicitDefaultValue || param.ExplicitDefaultValue == null)
                        continue;

                    switch (param.Name)
                    {
                        case "channel":
                            channel = Convert.ToInt32(param.ExplicitDefaultValue);
                            break;
                        case "encrypt":
                            encrypt = Convert.ToInt32(param.ExplicitDefaultValue);
                            break;
                        case "compress":
                            compress = Convert.ToInt32(param.ExplicitDefaultValue);
                            break;
                    }
                }
            }

            var ctorArgs = attr.ConstructorArguments;
            ushort id = 0;
            if (ctorArgs.Length > 0 && ctorArgs[0].Value != null) id = Convert.ToUInt16(ctorArgs[0].Value);
            if (ctorArgs.Length > 1 && ctorArgs[1].Value != null) channel = Convert.ToInt32(ctorArgs[1].Value);
            if (ctorArgs.Length > 2 && ctorArgs[2].Value != null) encrypt = Convert.ToInt32(ctorArgs[2].Value);
            if (ctorArgs.Length > 3 && ctorArgs[3].Value != null) compress = Convert.ToInt32(ctorArgs[3].Value);

            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Value.Value == null)
                    continue;

                switch (namedArg.Key)
                {
                    case "Channel":
                        channel = Convert.ToInt32(namedArg.Value.Value);
                        break;
                    case "Encrypt":
                        encrypt = Convert.ToInt32(namedArg.Value.Value);
                        break;
                    case "Compress":
                        compress = Convert.ToInt32(namedArg.Value.Value);
                        break;
                }
            }

            var classSyntax = (ClassDeclarationSyntax)context.TargetNode;
            var typeModel = CreateSerializableTypeModel(typeSymbol, classSyntax, ct);
            var assemblyName = IdentifierHelper.Sanitize(typeSymbol.ContainingAssembly.Name);

            return new ProtocolTypeModel(assemblyName, id, channel, encrypt, compress, typeModel);
        }

        private static SerializableTypeModel? TransformComplexTypeModel(GeneratorAttributeSyntaxContext context, CancellationToken ct)
        {
            if (context.TargetSymbol is not INamedTypeSymbol symbol || symbol.IsAbstract || symbol.IsGenericType)
                return null;
            
            var classSyntax = (ClassDeclarationSyntax)context.TargetNode;
            return CreateSerializableTypeModel(symbol, classSyntax, ct);
        }

        private static SerializableTypeModel CreateSerializableTypeModel(
            INamedTypeSymbol typeSymbol,
            ClassDeclarationSyntax classSyntax,
            CancellationToken ct)
        {
            var isPartial = classSyntax.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword));
            var members = ExtractSerializableMembers(typeSymbol, ct);
            var namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : typeSymbol.ContainingNamespace.ToDisplayString();

            var fullTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            return new SerializableTypeModel(
                namespaceName,
                typeSymbol.Name,
                fullTypeName,
                isPartial,
                members);
        }

        private static List<SerializableMemberModel> ExtractSerializableMembers(INamedTypeSymbol typeSymbol, CancellationToken ct)
        {
            var result = new List<SerializableMemberModel>();
            var typeChain = new List<INamedTypeSymbol>();
            var current = typeSymbol;

            // 상속 관계에 있는 클래스들 추출
            while (current != null && current.SpecialType != SpecialType.System_Object)
            {
                typeChain.Add(current);
                current = current.BaseType;
            }

            // 부모 클래스 멤버 -> 자식 클래스 멤버
            typeChain.Reverse();

            foreach (var member in typeChain.SelectMany(static t => t.GetMembers()))
            {
                ct.ThrowIfCancellationRequested();

                if (member.IsStatic || member.DeclaredAccessibility != Accessibility.Public)
                    continue;

                ITypeSymbol? memberType;
                bool canGet;
                bool canSet;

                switch (member)
                {
                    case IFieldSymbol { IsImplicitlyDeclared: true }:
                        continue;
                    case IFieldSymbol field:
                        memberType = field.Type;
                        canGet = true;
                        canSet = field is { IsReadOnly: false, IsConst: false };
                        break;
                    case IPropertySymbol { IsIndexer: true }:
                        continue;
                    case IPropertySymbol prop:
                        memberType = prop.Type;
                        canGet = prop.GetMethod is { DeclaredAccessibility: Accessibility.Public, IsStatic: false };
                        canSet = prop.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, IsInitOnly: false };
                        break;
                    default:
                        continue;
                }

                if (!canGet && !canSet)
                    continue;

                var isSupported = MemberCodeBuilder.TryCreateStatements(
                    member.Name,
                    memberType,
                    out var writeStatement,
                    out var readStatement,
                    out var resetStatement);

                result.Add(new SerializableMemberModel(
                    member.Name,
                    memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    isSupported,
                    canGet,
                    canSet,
                    writeStatement,
                    readStatement,
                    resetStatement));
            }

            return result;
        }
        
        private static void GenerateProtocolType(SourceProductionContext spc, ProtocolTypeModel typeModel)
        {
            if (!ValidateSerializableType(spc, typeModel.Type))
                return;

            var source = BuildProtocolTypeSource(typeModel);
            var hintName = BuildHintName(typeModel.Type, "Protocol.g.cs");
            spc.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
        }

        private static void GenerateComplexType(SourceProductionContext spc, SerializableTypeModel typeModel)
        {
            if (!ValidateSerializableType(spc, typeModel))
                return;

            var source = BuildComplexTypeSource(typeModel);
            var hintName = BuildHintName(typeModel, "ComplexType.g.cs");
            spc.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
        }

        private static bool ValidateSerializableType(SourceProductionContext spc, SerializableTypeModel typeModel)
        {
            if (string.IsNullOrEmpty(typeModel.Namespace))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.MissingNamespace,
                    Location.None,
                    typeModel.FullTypeName));
                return false;
            }
            
            if (!typeModel.IsPartial)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.MissingPartialModifier,
                    Location.None,
                    typeModel.FullTypeName));
                return false;
            }

            foreach (var member in typeModel.Members.Where(static member => !member.IsSupported))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.UnsupportedMemberType,
                    Location.None,
                    member.Name,
                    member.FullTypeName));
            }

            return true;
        }

        private static string BuildHintName(SerializableTypeModel type, string suffix)
        {
            return string.IsNullOrEmpty(type.Namespace)
                ? $"{type.TypeName}.{suffix}"
                : $"{type.Namespace}.{type.TypeName}.{suffix}";
        }

        private static string BuildProtocolTypeSource(ProtocolTypeModel typeModel)
        {
            var type = typeModel.Type;
            var methods = BuildSerializationMethods(type.Members);
            
            var sb = new StringBuilder();
            sb.AppendLine($"    partial class {type.TypeName} : {KnownSymbols.GlobalIProtocolData}");
            sb.AppendLine("    {");
            sb.AppendLine($"        public ushort Id => {typeModel.Id};");
            sb.AppendLine($"        public {KnownSymbols.GlobalChannelKind} Channel => ({KnownSymbols.GlobalChannelKind}){typeModel.Channel};");
            sb.AppendLine();
            sb.AppendLine(methods);
            sb.AppendLine("    }");
            
            return WrapNamespace(type.Namespace, sb.ToString());
        }

        private static string BuildComplexTypeSource(SerializableTypeModel typeModel)
        {
            var methods = BuildSerializationMethods(typeModel.Members);
            
            var sb = new StringBuilder();
            sb.AppendLine($"    partial class {typeModel.TypeName} : {KnownSymbols.GlobalINetSerializable}");
            sb.AppendLine("    {");
            sb.AppendLine(methods);
            sb.AppendLine("    }");

            return WrapNamespace(typeModel.Namespace, sb.ToString());
        }

        private static string BuildSerializationMethods(List<SerializableMemberModel> members)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine($"        public void Serialize(ref {KnownSymbols.GlobalNetWriter} writer)");
            sb.AppendLine("        {");
            foreach (var m in members.Where(static m => m is { IsSupported: true, SerializeGet: true }))
            {
                sb.AppendLine($"            {m.WriteStatement}");
            }

            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        public void Deserialize(ref {KnownSymbols.GlobalNetReader} reader)");
            sb.AppendLine("        {");
            foreach (var m in members.Where(static m => m is { IsSupported: true, SerializeSet: true }))
            {
                sb.AppendLine($"            {m.ReadStatement}");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        public void Reset()");
            sb.AppendLine("        {");
            foreach (var m in members.Where(static m => m is { IsSupported: true, SerializeSet: true }))
            {
                sb.AppendLine($"            {m.ResetStatement}");
            }

            sb.Append("        }");
            
            return sb.ToString();
        }

        private static string WrapNamespace(string ns, string body)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");

            if (string.IsNullOrEmpty(ns))
            {
                sb.Append(body);
                return sb.ToString();
            }
            
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.Append(body);
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string BuildProtocolPolicyRegistrySource(List<ProtocolPolicyModel> models)
        {
            var assemblyName = models[0].AssemblyName;
            var className = $"ProtocolPolicyRegistry_{assemblyName}";
            
            var sb = new StringBuilder();
            
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine($"[assembly: {KnownSymbols.GlobalProtocolPolicyRegistryAttr}(typeof(global::{KnownSymbols.GlobalNamespace}.{className}))]");
            sb.AppendLine();
            sb.AppendLine($"namespace {KnownSymbols.GlobalNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    internal sealed class {className} : {KnownSymbols.GlobalIProtocolPolicyRegistry}");
            sb.AppendLine("    {");
            sb.AppendLine($"        public void Register(global::System.Collections.Generic.IDictionary<ushort, {KnownSymbols.GlobalProtocolPolicyOverrides}> overrides)");
            sb.AppendLine("        {");

            foreach (var model in models)
            {
                sb.AppendLine($"             overrides[{model.Id}] = new {KnownSymbols.GlobalProtocolPolicyOverrides}(({KnownSymbols.GlobalProtocolPolicyToggle}){model.Encrypt}, ({KnownSymbols.GlobalProtocolPolicyToggle}){model.Compress});");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            
            return sb.ToString();
        }
    }
}
