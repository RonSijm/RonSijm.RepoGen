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
        var state = store.MappedPropertyStateEnabled && read.PropertyMappings is { Count: > 0 } ? read.PropertyMappings : null;
        if (state?.Any(binding => InMemoryWriteState.IsIdentity(entity.FindProperty(binding.PropertyName)!)) == true)
        {
            throw Invalid("Named write-state bindings cannot supply keys, unique-index members or foreign keys. Identity/ownership still requires ordinary explicit scalar assignments.");
        }
        if (properties.Any(property => !PortableWriteScalar(property.ClrType) ||
            !read.Members.Any(member => member.Name == InMemoryWriteState.Member(property.Name, state)) ||
            state?.Any(binding => binding.PropertyName == property.Name) != true &&
                property.PropertyInfo?.Name != property.Name && property.FieldInfo?.Name != property.Name))
            throw Invalid("Every EF scalar must have an explicit ordinary CLR scalar assignment, including keys, tokens and unassigned columns.");
        var translator = InMemoryWriteState.Translator(entity, state);
        var expression = translator?.Expand(write) ?? write;
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        new InMemoryQueryExpressionInspector(reasons).Visit(expression.Body);
        new InMemoryEntityMappingExpressionInspector(entity, read, reasons).Visit(expression.Body);
        new InMemoryMappingValueExpressionInspector(entity.Model, reasons, entity.ClrType).Visit(expression.Body);
        if (RepositoryQuerySourceRenderer.NavigationReferences(entity.Model, expression).Count != 0)
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
            InMemoryWriteState.Access(property.Name, state) + " = " + (property.ClrType == typeof(byte[]) ?
                "(row." + InMemoryWriteState.Access(property.Name, state) + " == null ? null! : global::System.Linq.Enumerable.ToArray(row." + InMemoryWriteState.Access(property.Name, state) + "))" : "row." + InMemoryWriteState.Access(property.Name, state)))) + " }";
        try
        {
            return new(ProjectionExpressionRenderer.RenderLambda(expression, ["entity", "saved"]), copy,
                "static row => " + ProjectionExpressionRenderer.RenderBody(key, ["row"], substitutions), InMemorySameScalars(properties, bindings: state), state);
        }
        catch (InvalidOperationException exception) { throw Invalid(exception.Message); }
    }

    private static string InMemorySameScalars(IEnumerable<IProperty> selected, IReadOnlyList<InMemoryValueEqualityModel>? policies = null,
        IReadOnlyList<InMemoryPropertyModel>? bindings = null)
        => "static (__repoGenLeft, __repoGenRight) => " + (selected.Any()
            ? string.Join(" && ", selected.Select(property => InMemoryValueEqualitySourceRenderer.Compare(policies, property.Name,
                "__repoGenLeft." + InMemoryWriteState.Access(property.Name, bindings), "__repoGenRight." + InMemoryWriteState.Access(property.Name, bindings)))) : "true");

}
