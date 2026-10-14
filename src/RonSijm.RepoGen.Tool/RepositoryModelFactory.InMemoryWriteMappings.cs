using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static InMemoryWriteMappingModel? CreateInMemoryWriteMapping(IEntityType entity, RepositoryEntityDesign definition,
        RepositoryGenerationSettingsModel settings, InMemoryEntityMappingModel? read)
    {
        if (!settings.InMemoryTestCommands || !definition.WriteEnabled || definition.InMemoryStore?.WriteMapping is not { } write) return null;
        InvalidOperationException Invalid(string reason) => new("RGD3601: Invalid MapInMemoryWrites for '" + entity.ClrType.FullName + "': " + reason);
        var store = definition.InMemoryStore;
        if (read is null || store.RowType == entity.ClrType || write.Parameters.Count != 2 || write.Parameters[0].Type != entity.ClrType ||
            write.Parameters[1].Type != store.RowType || write.ReturnType != store.RowType)
            throw Invalid("Use matching public storage/scalar maps and a candidate/saved-data write expression.");
        var properties = entity.GetProperties().OrderBy(static property => property.Name, StringComparer.Ordinal).ToArray();
        if (properties.Any(property => !PortableWriteScalar(property.ClrType) || !read.Members.Any(member => member.Name == property.Name) ||
            property.PropertyInfo?.Name != property.Name && property.FieldInfo?.Name != property.Name))
            throw Invalid("Every EF scalar must have an explicit ordinary CLR scalar assignment, including keys, tokens and unassigned columns.");
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        new InMemoryQueryExpressionInspector(reasons).Visit(write.Body);
        new InMemoryEntityMappingExpressionInspector(entity, read, reasons).Visit(write.Body);
        new InMemoryMappingValueExpressionInspector(entity.Model, reasons, entity.ClrType).Visit(write.Body);
        if (RepositoryQuerySourceRenderer.NavigationReferences(entity.Model, write).Count != 0)
            reasons.Add("Write adapters cannot traverse navigation slots; scalar writes do not persist a graph.");
        if (reasons.Count != 0) throw Invalid(string.Join(" ", reasons));

        // Translate only declared, same-type primary-key aliases. Never guess inverse arithmetic/conversions.
        var initializer = (MemberInitExpression)store.EntityMapping!.Body;
        var aliases = initializer.Bindings.OfType<MemberAssignment>()
            .Where(assignment => assignment.Expression is MemberExpression { Expression: ParameterExpression parameter } && parameter == store.EntityMapping.Parameters[0])
            .ToArray();
        var parts = store.KeySelector.Body is NewExpression compound ? compound.Arguments.ToArray() : [store.KeySelector.Body];
        var primary = entity.FindPrimaryKey()?.Properties.ToArray() ?? [];
        if (primary.Length == 0 || parts.Length != primary.Length) throw Invalid("The storage key must directly represent the complete EF primary key in declared order.");
        var replacements = new Dictionary<Expression, Expression>();
        var substitutions = new Dictionary<ParameterExpression, string>();
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index] is not MemberExpression { Expression: ParameterExpression parameter } part || parameter != store.KeySelector.Parameters[0] ||
                !aliases.Any(assignment => assignment.Member.Name == primary[index].Name && ((MemberExpression)assignment.Expression).Member == part.Member) ||
                part.Type != primary[index].ClrType)
                throw Invalid("Primary-key aliases must be direct, same-type public-row members, not transformed or reordered keys.");
            var replacement = Expression.Parameter(part.Type, "__repoGenKeyPart" + index);
            replacements.Add(part, replacement);
            substitutions.Add(replacement, "row." + EscapeIdentifier(primary[index].Name));
        }
        var key = Expression.Lambda(new InMemoryKeyPartReplacer(replacements).Visit(store.KeySelector.Body)!, store.KeySelector.Parameters);
        var copy = "static row => new " + read.ReadTypeName + " { " + string.Join(", ", properties.Select(property =>
            EscapeIdentifier(property.Name) + " = " + (property.ClrType == typeof(byte[]) ?
                "(row." + EscapeIdentifier(property.Name) + " == null ? null! : global::System.Linq.Enumerable.ToArray(row." + EscapeIdentifier(property.Name) + "))" : "row." + EscapeIdentifier(property.Name)))) + " }";
        try
        {
            return new(ProjectionExpressionRenderer.RenderLambda(write, ["entity", "saved"]), copy,
                "static row => " + ProjectionExpressionRenderer.RenderBody(key, ["row"], substitutions), InMemorySameScalars(properties));
        }
        catch (InvalidOperationException exception) { throw Invalid(exception.Message); }
    }

    private static string InMemorySameScalars(IEnumerable<IProperty> selected, IReadOnlyList<InMemoryValueEqualityModel>? policies = null)
        => "static (__repoGenLeft, __repoGenRight) => " + (selected.Any()
            ? string.Join(" && ", selected.Select(property => InMemoryValueEqualitySourceRenderer.Compare(policies, property.Name,
                "__repoGenLeft." + EscapeIdentifier(property.Name), "__repoGenRight." + EscapeIdentifier(property.Name)))) : "true");

}
