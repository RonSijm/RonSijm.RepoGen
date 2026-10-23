using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static List<InMemoryValueEqualityModel>? CreateInMemoryValueEqualities(IEntityType entity,
        RepositoryEntityDesign definition, RepositoryGenerationSettingsModel settings)
    {
        if (!settings.InMemoryTestCommands || !definition.WriteEnabled || definition.InMemoryStore?.ValueEqualities is not { Count: > 0 } declarations) return null;
        var policies = new List<InMemoryValueEqualityModel>();
        foreach (var declaration in declarations.OrderBy(static declaration => declaration.PropertyName, StringComparer.Ordinal))
        {
            InvalidOperationException Invalid(string reason) => new("RGD4101: Invalid fixture value equality for '" + entity.ClrType.FullName + "." + declaration.PropertyName + "': " + reason);
            var property = entity.FindProperty(declaration.PropertyName);
            if (property is null || !PortableWriteScalar(property.ClrType) || !property.ClrType.IsVisible ||
                property.IsKey() || property.GetContainingForeignKeys().Any() || property.GetContainingIndexes().Any(static index => index.IsUnique) ||
                property.ValueGenerated != ValueGenerated.Never || property.IsConcurrencyToken && property.ClrType == typeof(byte[]))
                throw Invalid("Select an ordinary mapped, non-generated scalar; keys, unique-index members, foreign keys and binary concurrency tokens retain their own identity/provider rules.");
            var expression = declaration.Equality;
            if (expression.ReturnType != typeof(bool) || expression.Parameters.Count != 2 || expression.Parameters.Any(parameter => parameter.Type != property.ClrType))
                throw Invalid("Use a Boolean expression with exactly two inputs matching the mapped scalar type.");
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            new InMemoryQueryExpressionInspector(reasons).Visit(expression);
            new InMemoryTranslationReplacementInspector(reasons).Visit(expression);
            var usage = new InMemoryTranslationParameterUsage(); usage.Visit(expression.Body);
            if (expression.Parameters.Any(parameter => !usage.Parameters.Contains(parameter))) reasons.Add("Both scalar inputs must remain referenced.");
            if (reasons.Count != 0) throw new InvalidOperationException("RGD4102: Fixture value equality requires self-contained portable semantics: " + string.Join(" ", reasons));
            var type = CSharpTypeName(property.ClrType) + (property.IsNullable && !property.ClrType.IsValueType ? "?" : "");
            policies.Add(new(property.Name, type, ProjectionExpressionRenderer.RenderBody(expression, ["left", "right"]),
                "global::" + settings.ScriptedTestDoubleNamespace + ".__RepoGen" + entity.ClrType.Name + "ValueEqualities"));
        }
        return policies;
    }

    private static bool HasExplicitInMemoryEquality(IProperty property, IReadOnlyList<InMemoryValueEqualityModel>? policies, IModel? metadata)
        => policies?.Any(policy => policy.PropertyName == property.Name) == true && InMemoryRelationshipMetadata.CanDeclareScalarEquality(property, metadata);
}
