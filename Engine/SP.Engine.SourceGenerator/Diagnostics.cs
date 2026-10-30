using Microsoft.CodeAnalysis;

namespace SP.Engine.SourceGenerator
{
    internal static class Diagnostics
    {
        public static readonly DiagnosticDescriptor MissingPartialModifier = new(
            id: "SPE001",
            title: "Serializable protocol or sub-type class must be partial",
            messageFormat: "Type '{0}' is used in protocol serialization and must be declared 'partial' for compile-time code generation",
            category: "SP.Engine.SourceGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateProtocolHandlerId = new(
            id: "SPE002",
            title: "Duplicate ProtocolHandler ID",
            messageFormat: "Duplicate ProtocolHandler ID '{0}' for context type '{1}' in handler '{2}'",
            category: "SP.Engine.SourceGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedMemberType = new(
            id: "SPE003",
            title: "Unsupported member type for network serialization",
            messageFormat: "Member '{0}' of type '{1}' is not supported by network serialization",
            category: "SP.Engine.SourceGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateProtocolId = new(
            id: "SPE004",
            title: "Duplicate ProtocolData ID",
            messageFormat: "Duplicate ProtocolData ID '{0}' in protocol type '{1}'",
            category: "SP.Engine.SourceGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingNamespace = new(
            id: "SPE005",
            title: "Missing Namespace in Protocol Class",
            messageFormat: "The protocol class '{0}' must be declared inside a namespace to prevent naming collisions",
            category: "SP.Engine.SourceGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
