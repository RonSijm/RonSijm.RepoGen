using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static System.Collections.ObjectModel.ReadOnlyCollection<InMemoryGeneratedValueModel>? CreateInMemoryGeneratedValues(IEntityType entity,
        RepositoryInMemoryStoreDesign store, RepositoryGenerationSettingsModel settings, InMemoryEntityMappingModel? read, IModel? metadata)
    {
        if (!settings.InMemoryTestCommands || store.GeneratedValues.Count == 0 && !store.ModelDefaultsEnabled) return null;
        var state = store.MappedPropertyStateEnabled && store.WriteMapping is not null ? read?.PropertyMappings : null;
        var translator = InMemoryWriteState.Translator(entity, state);
        var values = new List<InMemoryGeneratedValueModel>();
        foreach (var adapter in store.GeneratedValues)
        {
            InvalidOperationException Invalid(string reason) => new("RGD3501: Invalid fixture generated value '" + entity.ClrType.FullName + "." + adapter.PropertyName + "': " + reason);
            var property = entity.FindProperty(adapter.PropertyName);
            var mapped = store.RowType != entity.ClrType && store.EntityMapping is not null && store.WriteMapping is not null;
            if (property is null || !PortableWriteScalar(property.ClrType) || (!mapped && (store.RowType != entity.ClrType || !entity.ClrType.IsVisible ||
                property.PropertyInfo?.GetMethod?.IsPublic != true || property.PropertyInfo.SetMethod?.IsPublic != true ||
                property.PropertyInfo.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)))))
                throw Invalid("Use an ordinary scalar with an entity-shaped public setter or explicit public scalar/write maps.");
            if (property.ValueGenerated == ValueGenerated.Never || property.IsKey() && (property.ValueGenerated & ValueGenerated.OnUpdate) != 0)
                throw Invalid("The model must declare insert/update generation; update-generated keys cannot be simulated.");
            var onCreate = (property.ValueGenerated & ValueGenerated.OnAdd) != 0;
            var onUpdate = (property.ValueGenerated & ValueGenerated.OnUpdate) != 0;
            if (adapter.Value.ReturnType != property.ClrType) throw Invalid("Factory and mapped property types must match exactly.");
            if (property.GetBeforeSaveBehavior() == PropertySaveBehavior.Throw)
                throw Invalid("Throw-on-explicit-insert generated properties require an override.");
            var conditional = property.GetBeforeSaveBehavior() != PropertySaveBehavior.Ignore;
            var defaultSentinel = Equals(property.Sentinel, property.ClrType.IsValueType ? Activator.CreateInstance(property.ClrType) : null);
            var sentinel = defaultSentinel ? "default(" + CSharpTypeName(property.ClrType) + ")" : InMemoryScalarConstantRenderer.RenderTyped(property.Sentinel, property.ClrType);
            if (onCreate && conditional && (sentinel is null || !defaultSentinel && InMemorySentinelHasCustomComparison(property, metadata)))
                throw Invalid("Custom insert sentinels require portable scalar constants without custom conversion/comparison.");
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            var expression = translator?.Expand(adapter.Value) ?? adapter.Value;
            new InMemoryQueryExpressionInspector(reasons).Visit(expression);
            if (mapped)
            {
                new InMemoryEntityMappingExpressionInspector(entity, read!, reasons).Visit(expression);
                new InMemoryMappingValueExpressionInspector(entity.Model, reasons, entity.ClrType, allowScalarSequences: true).Visit(expression.Body);
            }
            if (RepositoryQuerySourceRenderer.NavigationReferences(entity.Model, expression).Count != 0) reasons.Add("Navigation-dependent value factories are not supported.");
            if (reasons.Count != 0) throw Invalid(string.Join(" ", reasons));
            try
            {
                values.Add(new(adapter.PropertyName, ProjectionExpressionRenderer.RenderBody(expression, ["__repoGenEntity", "__repoGenRows"]),
                    conditional ? "global::RonSijm.RepoGen.Testing.InMemoryValueEquality.AreEqual(__repoGenEntity." + InMemoryWriteState.Access(adapter.PropertyName, state) +
                        ", " + (sentinel ?? "default(" + CSharpTypeName(property.ClrType) + ")") + ")" : "true", onCreate, onUpdate,
                    CustomSentinelSource: onCreate && conditional && !defaultSentinel ? sentinel : null,
                    StateMemberName: state?.SingleOrDefault(binding => binding.PropertyName == adapter.PropertyName)?.MemberName));
            }
            catch (InvalidOperationException exception) { throw Invalid(exception.Message); }
        }
        foreach (var property in entity.GetProperties().OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            if (store.GeneratedValues.Any(adapter => adapter.PropertyName == property.Name) || InMemoryModelDefault(property, store, metadata) is not { } source) continue;
            var defaultSentinel = Equals(property.Sentinel, property.ClrType.IsValueType ? Activator.CreateInstance(property.ClrType) : null);
            var sentinel = defaultSentinel ? "default(" + CSharpTypeName(property.ClrType) + ")" : InMemoryScalarConstantRenderer.RenderTyped(property.Sentinel, property.ClrType);
            if (sentinel is null) throw new InvalidOperationException("RGD3501: Model default '" + entity.ClrType.FullName + "." + property.Name + "' has a nonportable insert sentinel; declare an explicit adapter/override.");
            if (store.RowType != entity.ClrType && read?.Members.Any(member => member.Name == InMemoryWriteState.Member(property.Name, state)) != true)
                throw new InvalidOperationException("RGD3501: Model defaults require complete scalar read/write mappings for '" + property.Name + "'.");
            values.Add(new(property.Name, source, "global::RonSijm.RepoGen.Testing.InMemoryValueEquality.AreEqual(__repoGenEntity." + InMemoryWriteState.Access(property.Name, state) + ", " + sentinel + ")",
                true, false, source, defaultSentinel ? null : sentinel,
                state?.SingleOrDefault(binding => binding.PropertyName == property.Name)?.MemberName));
        }
        return values.Count == 0 ? null : values.AsReadOnly();
    }
}
