using Microsoft.CodeAnalysis;

namespace RonSijm.RepoGen.Generator;

internal static class DiagnosticDescriptors
{
    private const string Category = "RonSijm.RepoGen";

    public static readonly DiagnosticDescriptor MissingMember = new(
        "RG001",
        "Projection member is missing from the entity",
        "Projection '{0}' contains member '{1}', but entity '{2}' does not contain a readable instance member with that name",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor AmbiguousMethodName = new(
        "RG002",
        "Projection method name is ambiguous",
        "Multiple projections for entity '{0}' generate method name '{1}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor IncompatibleMember = new(
        "RG003",
        "Projection member type is incompatible",
        "Projection member '{0}.{1}' of type '{2}' cannot be assigned from '{3}.{1}' of type '{4}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor InvalidSelectorEntity = new(
        "RG004",
        "Invalid selector entity",
        "Projection '{0}' references entity '{1}', which is not a valid selector-enabled entity",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor InvalidSelectorDeclaration = new(
        "RG004",
        "Invalid selector entity",
        "Selectors cannot be generated for '{0}': only top-level, non-generic entity classes are supported",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor UnsupportedProjection = new(
        "RG005",
        "Unsupported projection",
        "Projection '{0}' cannot be generated: {1}",
        Category,
        DiagnosticSeverity.Error,
        true);
}