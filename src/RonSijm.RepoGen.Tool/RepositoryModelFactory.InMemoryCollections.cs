using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static InMemoryCollectionCommandModel[] CreateInMemoryCollections(IEntityType parent, RepositoryCommandDesign command,
        RepositoryGenerationSettingsModel settings, IModel? metadata, RepositoryDesign? design, SortedSet<string> reasons,
        InMemoryTranslationExpressions? translations = null)
        => command.Collections.Select(collection =>
        {
            // Production validation has already established a direct principal-to-leaf-child navigation.
            var navigation = parent.FindNavigation(((MemberExpression)collection.Navigation.Body).Member.Name)!;
            var child = navigation.TargetEntityType;
            var definition = design?.Entities.SingleOrDefault(entity => entity.EntityType == child.ClrType);
            var childReasons = new SortedSet<string>(StringComparer.Ordinal);
            var store = definition?.InMemoryStore;
            if (definition is null || !definition.RepositoryEnabled || !definition.WriteEnabled || definition.TestDoubleEnabled == false || store is null ||
                child.ClrType.IsNested || child.ClrType.IsGenericType)
            {
                reasons.Add("Collection '" + navigation.Name + "' " + (child.ClrType.IsNested || child.ClrType.IsGenericType
                    ? "has a nested/generic child CLR type excluded from repository generation; no fixture store is emitted."
                    : "requires an enabled, selected writable child HasInMemoryStore."));
                return new InMemoryCollectionCommandModel(navigation.Name, "", "", "", "", "", null, new(false, [], "", "", []));
            }
            var read = store.WriteMapping is null ? null : CreateInMemoryEntityMapping(child, definition, settings, design);
            var writeMapping = CreateInMemoryWriteMapping(child, definition, settings, read);
            var mapped = writeMapping is not null;
            var state = writeMapping?.MappedPropertyState;
            if (!mapped && (store.RowType != child.ClrType || !child.ClrType.IsVisible))
                childReasons.Add("Use a public entity-shaped store or explicit MapInMemoryEntity and MapInMemoryWrites for child state.");
            var modelFilter = CreateInMemoryModelQueryFilter(child, command.InMemoryCollectionModelQueryFilters, childReasons, translations, design);
            if (child.GetQueryFilter() is not null && modelFilter is null || child.GetNavigations().Any(static edge => edge.IsEagerLoaded) || child.GetSkipNavigations().Any(static edge => edge.IsEagerLoaded))
                childReasons.Add("Active EF filters/AutoInclude child semantics require a script; declare portable designer visibility instead.");
            var properties = child.GetProperties().OrderBy(static property => property.Name, StringComparer.Ordinal).ToArray();
            var equalities = CreateInMemoryValueEqualities(child, definition, settings);
            if (properties.Any(property => !PortableWriteScalar(property.ClrType) ||
                !mapped && (property.PropertyInfo?.GetMethod?.IsPublic != true || !property.ClrType.IsVisible)))
                childReasons.Add("Child write state needs ordinary readable CLR scalars or complete explicit public scalar maps.");
            if (properties.Any(property => InMemoryRelationshipMetadata.RequiresCustomEquality(property, metadata) && !HasExplicitInMemoryEquality(property, equalities, metadata)) ||
                navigation.ForeignKey.PrincipalKey.Properties.Any(property => InMemoryRelationshipMetadata.RequiresCustomEquality(property, metadata)))
                childReasons.Add("Custom relationship/scalar conversion or comparison is not simulated.");
            if (properties.Any(property => (collection.CreateMapping is not null && (property.ValueGenerated & ValueGenerated.OnAdd) != 0 ||
                collection.Updates.Count != 0 && (property.ValueGenerated & ValueGenerated.OnUpdate) != 0) &&
                !HasInMemoryValueAdapter(property, store, metadata)))
                childReasons.Add("Generated child values require explicit fixture factories, not inferred persistence defaults.");
            if (!mapped)
            {
                var keyParts = store.KeySelector.Body is NewExpression compound ? compound.Arguments.ToArray() : [store.KeySelector.Body];
                var keyNames = keyParts.Select(part => part is MemberExpression { Expression: ParameterExpression parameter } member && parameter == store.KeySelector.Parameters[0] ? member.Member.Name : "");
                if (!keyNames.SequenceEqual(child.FindPrimaryKey()!.Properties.Select(static property => property.Name)))
                    childReasons.Add("The child fixture key must directly select the complete EF primary key in declared order.");
            }
            var foreignKey = navigation.ForeignKey;
            if (foreignKey.Properties.Any(property => !mapped && (property.PropertyInfo?.SetMethod?.IsPublic != true ||
                property.PropertyInfo.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)))) ||
                foreignKey.PrincipalKey.Properties.Any(property => property.PropertyInfo?.GetMethod?.IsPublic != true))
                childReasons.Add("Ownership keys need readable parent scalars and writable child scalars; shadow/backing-field relationships require a script.");
            var expressions = new List<Expression> { collection.Items, collection.ItemKey, collection.ChildKey };
            if (collection.Condition is not null) expressions.Add(collection.Condition);
            expressions.AddRange(collection.Updates.SelectMany(static update => (update.Property is null ? new[] { update.Value } : new[] { update.Property, update.Value })
                .Concat(update.Condition is null ? [] : new[] { update.Condition })));
            expressions.AddRange(collection.ExpectedVersions.SelectMany(static version => version.Property is null ? new[] { version.Value } : new[] { version.Property, version.Value }));
            if (collection.ExpectedVersions.Any(version => version.MappedPropertyName is { } name &&
                child.FindProperty(name)?.PropertyInfo?.GetMethod?.IsPublic != true && state?.Any(binding => binding.PropertyName == name) != true))
                childReasons.Add("Named child concurrency tokens without public CLR members require a typed override; fixture shadow/backing-field token state is not simulated.");
            expressions.AddRange(definition.VisibilityPolicies.Select(static policy => policy.Predicate));
            if (modelFilter is { } accepted) expressions.Add(accepted.Expression);
            if (collection.CreateMapping?.Body is MemberInitExpression initializer)
                expressions.AddRange(initializer.Bindings.OfType<MemberAssignment>().Select(static assignment => assignment.Expression));
            var inspector = new InMemoryQueryExpressionInspector(childReasons);
            foreach (var original in expressions)
            {
                var expression = translations?.Visit(original) ?? original;
                inspector.Visit(expression);
                if (RepositoryQuerySourceRenderer.NavigationReferences(child.Model, expression).Count != 0)
                    childReasons.Add("Navigation-dependent child selection/values require a script; only owner-scoped scalar reconciliation is simulated.");
                if (mapped)
                {
                    new InMemoryEntityMappingExpressionInspector(child, read!, childReasons).Visit(expression);
                    new InMemoryMappingValueExpressionInspector(child.Model, childReasons, child.ClrType, allowScalarSequences: true).Visit(expression);
                }
            }
            var merges = properties.Where(property => !property.IsKey() && (mapped || property.PropertyInfo?.SetMethod?.IsPublic == true &&
                !property.PropertyInfo.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit))))
                .Select(static property => property.Name).ToArray();
            if (collection.Updates.Any(update => !merges.Contains(update.MappedPropertyName ??
                    (update.Property?.Body as MemberExpression)?.Member.Name ?? "", StringComparer.Ordinal)))
                childReasons.Add("Child patches require non-init scalar setters and cannot change keys.");
            if (collection.Updates.Any(update => update.MappedPropertyName is { } name && child.FindProperty(name) is { } property &&
                (property.PropertyInfo?.GetMethod?.IsPublic != true || property.PropertyInfo.SetMethod?.IsPublic != true || property.IsIndexerProperty()) &&
                state?.Any(binding => binding.PropertyName == name) != true))
                childReasons.Add("Named non-public/shadow/indexer child setters require complete MapInMemoryWrites(useMappedPropertyState: true) and exact public-data property bindings; parent/read permission is not child write state.");
            var rowType = mapped ? read!.ReadTypeName : CSharpTypeName(child.ClrType);
            var creation = CreateInMemoryCollectionCreate(child, collection, store, rowType, foreignKey, mapped, childReasons, translations, metadata, state);
            foreach (var reason in childReasons) reasons.Add("Collection '" + navigation.Name + "': " + reason);
            var scope = "(entity, child) => " + string.Join(" && ", foreignKey.Properties.Select((property, index) =>
                "global::RonSijm.RepoGen.Testing.InMemoryValueEquality.AreEqual(entity." + EscapeIdentifier(foreignKey.PrincipalKey.Properties[index].Name) + ", child." + EscapeIdentifier(property.Name) + ")"));
            var bind = "(entity, child) => { " + string.Join(" ", foreignKey.Properties.Select((property, index) =>
                "child." + EscapeIdentifier(property.Name) + " = entity." + EscapeIdentifier(foreignKey.PrincipalKey.Properties[index].Name) + ";")) + " }";
            return new(navigation.Name, rowType, EscapeIdentifier(mapped ? "__RepoGen" + child.ClrType.Name + "Writes" : child.ClrType.Name + "Store"),
                CSharpTypeName(store.KeyType), scope, bind, creation,
                new(childReasons.Count == 0, childReasons.ToArray(), InMemorySameScalars(properties, equalities, state), InMemorySameScalars(properties.Where(static property => property.IsConcurrencyToken), equalities, state), merges,
                    ValueEqualities: equalities, ModelQueryFilter: modelFilter is { } filter ? new(filter.Model,
                        ProjectionExpressionRenderer.RenderBody(filter.Expression, filter.Expression.Parameters.Count == 1
                            ? ["child"] : ["child", InMemoryModelFilterContextExpressions.KeyVariable])) : null, MappedPropertyState: state));
        }).ToArray();

    private static string? CreateInMemoryCollectionCreate(IEntityType child, RepositoryCollectionReconciliationDesign collection,
        RepositoryInMemoryStoreDesign store, string rowType, IForeignKey foreignKey, bool mapped, SortedSet<string> reasons,
        InMemoryTranslationExpressions? translations = null, IModel? metadata = null, IReadOnlyList<InMemoryPropertyModel>? state = null)
    {
        if (collection.CreateMapping?.Body is not MemberInitExpression initializer) return null;
        var assigned = initializer.Bindings.OfType<MemberAssignment>().ToDictionary(static binding => binding.Member.Name);
        var values = new List<string>();
        foreach (var property in child.GetProperties().OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            if (!mapped && (property.PropertyInfo?.SetMethod?.IsPublic != true ||
                property.PropertyInfo.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit))))
                reasons.Add("Child creation needs public non-init scalar setters or complete public write maps.");
            if (assigned.TryGetValue(property.Name, out var assignment))
            {
                var value = ProjectionExpressionRenderer.RenderBody(Expression.Lambda(translations?.Visit(assignment.Expression) ?? assignment.Expression, collection.CreateMapping.Parameters), ["item"]);
                if (!property.IsNullable && !property.ClrType.IsValueType && assignment.Expression is ConstantExpression { Value: null }) value += "!";
                values.Add(InMemoryWriteState.Access(property.Name, state) + " = " + value);
            }
            else
            {
                if (!foreignKey.Properties.Contains(property) && ((property.ValueGenerated & ValueGenerated.OnAdd) == 0 ||
                    !HasInMemoryValueAdapter(property, store, metadata)))
                    reasons.Add("Child insertion must explicitly assign scalar '" + property.Name + "' or declare its generated-value factory; constructor/database defaults are not simulated.");
                if (!foreignKey.Properties.Contains(property) && property.GetBeforeSaveBehavior() != PropertySaveBehavior.Ignore && !Equals(property.Sentinel, property.ClrType.IsValueType ? Activator.CreateInstance(property.ClrType) : null))
                    reasons.Add("Child insertion must explicitly assign custom-sentinel property '" + property.Name + "'; field initializers are not inferred.");
                values.Add(InMemoryWriteState.Access(property.Name, state) + " = default(" + CSharpTypeName(property.ClrType) + ")!");
            }
        }
        return "new " + rowType + " { " + string.Join(", ", values) + " }";
    }
}
