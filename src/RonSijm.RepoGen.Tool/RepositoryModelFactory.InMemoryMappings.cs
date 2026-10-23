using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static InMemoryEntityMappingModel? CreateInMemoryEntityMapping(IEntityType entity, RepositoryEntityDesign? design,
        RepositoryGenerationSettingsModel settings, RepositoryDesign? fullDesign)
    {
        if ((!settings.InMemoryTestQueries && !(settings.InMemoryTestCommands && design?.WriteEnabled == true && design.InMemoryStore?.WriteMapping is not null)) ||
            design?.InMemoryStore?.EntityMapping is not { } mapping) return null;
        const string diagnostic = "RGD3203: Invalid MapInMemoryEntity for '";
        InvalidOperationException Invalid(string reason) => new(diagnostic + entity.ClrType.FullName + "': " + reason);
        if (mapping.Parameters.Count != 1 || mapping.Parameters[0].Type != design.InMemoryStore.RowType ||
            mapping.Body is not MemberInitExpression initializer || initializer.Type != entity.ClrType ||
            initializer.NewExpression.Arguments.Count != 0 || initializer.Bindings.Count == 0)
            throw Invalid("Use a parameterless entity initializer containing explicit scalar assignments.");

        var members = new List<InMemoryEntityMemberModel>();
        var assignments = new List<string>();
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        var valueInspector = new InMemoryMappingValueExpressionInspector(entity.Model, reasons);
        var readName = "__RepoGen" + entity.ClrType.Name + "ReadRow";
        foreach (var binding in initializer.Bindings)
        {
            if (binding is not MemberAssignment assignment)
                throw Invalid("Only direct scalar member assignments are supported.");
            var property = entity.GetProperties().SingleOrDefault(property => property.PropertyInfo == binding.Member || property.FieldInfo == binding.Member);
            if (property is null)
                throw Invalid("Member '" + binding.Member.Name + "' is not an EF scalar property. Navigation/computed members cannot be mapped.");
            if (!property.ClrType.IsVisible || property.ClrType.ContainsGenericParameters || property.ClrType.IsByRefLike)
                throw Invalid("Scalar member '" + binding.Member.Name + "' needs a public, closed type in the separate testing output.");
            if (valueInspector.ContainsEntity(property.ClrType))
                throw Invalid("Scalar member '" + binding.Member.Name + "' cannot expose a persistence entity type.");
            if (binding.Member.Name == readName || binding.Member.Name == "Finalize")
                throw Invalid("Scalar member '" + binding.Member.Name + "' collides with a reserved read-row declaration.");
            if (members.Any(member => member.Name == binding.Member.Name))
                throw Invalid("Scalar member '" + binding.Member.Name + "' is assigned more than once.");
            new InMemoryQueryExpressionInspector(reasons).Visit(assignment.Expression);
            valueInspector.Visit(assignment.Expression);
            if (reasons.Count != 0) throw Invalid(string.Join(" ", reasons));
            var value = Expression.Lambda(assignment.Expression, mapping.Parameters);
            try
            {
                assignments.Add(EscapeIdentifier(binding.Member.Name) + " = " + ProjectionExpressionRenderer.RenderBody(value, ["row"]));
            }
            catch (InvalidOperationException exception) { throw Invalid(exception.Message); }
            members.Add(new(binding.Member.Name, CSharpTypeName(property.ClrType, property.IsNullable)));
        }

        var propertyMappings = settings.InMemoryTestQueries || settings.InMemoryTestCommands && design.WriteEnabled &&
            design.InMemoryStore is { WriteMapping: not null, MappedPropertyStateEnabled: true }
                ? CreateInMemoryProperties(entity, design.InMemoryStore) : [];
        foreach (var property in propertyMappings)
        {
            members.Add(new(property.MemberName, property.TypeName));
            assignments.Add(property.MemberName + " = " + property.ValueSource);
        }
        var readType = "global::" + settings.ScriptedTestDoubleNamespace + "." + readName;
        return new(readType, "static row => new " + readType + " { " + string.Join(", ", assignments) + " }", members,
            CreateInMemoryMappedNavigationMembers(entity, design, settings, fullDesign), propertyMappings.Count == 0 ? null : propertyMappings);
    }
}
