using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static void ValidateInMemoryNavigationDeclarations(IModel model, RepositoryDesign? design, RepositoryGenerationSettingsModel settings)
    {
        if (!settings.InMemoryTestQueries) return;
        foreach (var definition in design?.Entities ?? [])
        {
            if (!definition.RepositoryEnabled || definition.TestDoubleEnabled == false || !definition.ReadEnabled && !definition.WriteEnabled) continue;
            if (definition.InMemoryStore?.RelationshipComparers.Count > 0)
                CreateInMemoryRelationshipComparers(model.FindEntityType(definition.EntityType) ??
                    throw new InvalidOperationException("RGD4501: Relationship comparison requires a mapped entity."), definition);
            foreach (var declaration in definition.InMemoryNavigations)
            {
                var entity = model.FindEntityType(definition.EntityType);
                var navigation = (INavigationBase?)entity?.FindNavigation(declaration.Name) ?? entity?.FindSkipNavigation(declaration.Name);
                if (navigation is null || navigation.PropertyInfo != declaration.Member && navigation.FieldInfo != declaration.Member)
                    throw new InvalidOperationException("RGD3304: HasInMemoryNavigation '" + definition.EntityType.FullName + "." + declaration.Name + "' must select the actual mapped navigation member.");
                if (declaration.PreserveMissingTargets && navigation is not ISkipNavigation { Inverse.ForeignKey.IsRequired: false })
                    throw new InvalidOperationException("RGD4401: PreserveMissingTargets requires an explicitly mapped many-to-many navigation with an optional target FK; ordinary references/collections and required targets do not have this null-slot policy.");
            }
        }
    }

    private static InMemoryNavigationModel? CreateInMemoryNavigations(IEntityType owner, RepositoryQueryDesign? query,
        RepositoryDesign? design, IncludeModel[] includes, IEnumerable<Expression> expressions,
        InMemoryQuerySourceModel? source, SortedSet<string> reasons, IModel? relationshipMetadata,
        RepositoryGenerationSettingsModel settings, List<(IEntityType Entity, InMemoryEntityMappingModel Mapping)> mappings,
        List<(LambdaExpression Expression, InMemoryModelQueryFilterModel Model)>? modelFilters = null,
        InMemoryTranslationExpressions? translations = null, IEnumerable<Expression>? valueExpressions = null,
        Func<IEntityType, RepositoryEntityDesign?, bool>? filterPermission = null)
    {
        var expressionList = expressions.ToArray();
        var references = expressionList.SelectMany(expression => RepositoryQuerySourceRenderer.NavigationReferences(owner.Model, expression)).ToHashSet();
        if (design?.Entities.Any(static entity => entity.InMemoryNavigations.Count != 0) != true)
        {
            if (includes.Length != 0) reasons.Add("Include/load-profile paths require relational navigation loading and are not simulated.");
            if (references.Count != 0) reasons.Add("Navigation traversal/relationship fixup is not simulated. Seeded graphs do not establish relational visibility or join behavior.");
            return null;
        }
        foreach (var path in includes.SelectMany(static include => include.Paths ?? []))
        {
            var current = owner;
            foreach (var name in path.Path.Split('.'))
            {
                var navigation = (INavigationBase?)current.FindNavigation(name) ?? current.FindSkipNavigation(name);
                if (navigation is null) break; // Production loading validation owns path diagnostics.
                references.Add(navigation);
                current = navigation.TargetEntityType;
            }
            if (path.FilterSource is not null)
                reasons.Add("Filtered Include/load-profile paths need provider-specific loading semantics; fixture navigation loading supports unfiltered paths only.");
        }
        if (references.Count == 0) return null;

        var types = references.SelectMany(static navigation => new[] { navigation.DeclaringEntityType, navigation.TargetEntityType })
            .Concat(references.OfType<ISkipNavigation>().Select(static navigation => navigation.JoinEntityType)
                .Where(static entity => !entity.HasSharedClrType && !entity.IsOwned() && !entity.ClrType.IsNested && !entity.ClrType.IsGenericType))
            .Concat(source is null ? [owner] : source.Roots.Select(root => owner.Model.GetEntityTypes().Single(entity => CSharpTypeName(entity.ClrType) == root.EntityTypeName)))
            .Distinct().OrderBy(static entity => entity.ClrType.FullName, StringComparer.Ordinal).ToArray();
        var roots = new List<InMemoryNavigationRootModel>();
        var mappedRoots = new Dictionary<IEntityType, InMemoryEntityMappingModel?>();
        foreach (var entity in types)
        {
            var definition = design?.Entities.FirstOrDefault(definition => definition.EntityType == entity.ClrType);
            InMemoryEntityMappingModel? mapping = null;
            if (definition is null || !definition.RepositoryEnabled || definition.TestDoubleEnabled == false ||
                !definition.ReadEnabled && !definition.WriteEnabled || definition.InMemoryStore is null || entity.ClrType.IsNested || entity.ClrType.IsGenericType ||
                entity.IsOwned() || entity.HasSharedClrType)
                reasons.Add("Navigation source '" + entity.ClrType.FullName + "' requires HasInMemoryStore on an enabled, selected ordinary test-double entity.");
            else if (definition.InMemoryStore.RowType != entity.ClrType)
            {
                mapping = CreateInMemoryEntityMapping(entity, definition, settings, design);
                if (mapping is null) reasons.Add("Navigation source '" + entity.ClrType.FullName + "' requires an explicit MapInMemoryEntity for its public data-shape store.");
                else mappings.Add((entity, mapping));
            }
            else if (!entity.ClrType.IsVisible)
                reasons.Add("Navigation source '" + entity.ClrType.FullName + "' requires a public entity-shaped store or an explicitly mapped public data shape.");
            // Saved results supply their own root permission. Related datasets still need explicit
            // entity permission; read flags never implicitly enable command/result filtering.
            var filterEnabled = filterPermission?.Invoke(entity, definition) ??
                (query is not null && definition?.InMemoryModelQueryFilterNavigationsEnabled == true);
            var modelFilter = query?.IgnoreQueryFilters != true
                ? CreateInMemoryModelQueryFilter(entity, filterEnabled, reasons, translations, design) : null;
            if (modelFilter is { } accepted && modelFilters is not null &&
                !modelFilters.Any(filter => filter.Model.EntityTypeName == accepted.Model.EntityTypeName))
                modelFilters.Add(accepted);
            roots.Add(new(CSharpTypeName(entity.ClrType), "__repoGenNavigationRows" + roots.Count,
                mapping is null ? null : CSharpTypeName(definition!.InMemoryStore!.RowType), mapping, modelFilter?.Model));
            mappedRoots.Add(entity, mapping);
            ValidateInMemoryEntityPolicies(entity, query, reasons, modelFilter is not null);
        }
        var variables = types.Select((entity, index) => (entity, roots[index].VariableName)).ToDictionary(static entry => entry.entity, static entry => entry.VariableName);

        var edges = new List<InMemoryNavigationEdgeModel>();
        foreach (var navigation in references.OrderBy(static navigation => navigation.DeclaringEntityType.ClrType.FullName, StringComparer.Ordinal).ThenBy(static navigation => navigation.Name, StringComparer.Ordinal))
        {
            var name = navigation.DeclaringEntityType.ClrType.FullName + "." + navigation.Name;
            var definition = design?.Entities.FirstOrDefault(definition => definition.EntityType == navigation.DeclaringEntityType.ClrType);
            var declaration = definition?.InMemoryNavigations.SingleOrDefault(declaration => declaration.Name == navigation.Name);
            if (declaration is null)
                reasons.Add("Navigation '" + name + "' needs an explicit HasInMemoryNavigation declaration; seeded graphs do not establish relational behavior.");
            if (navigation is ISkipNavigation skip && (skip.JoinEntityType.HasSharedClrType || skip.JoinEntityType.IsOwned() ||
                skip.JoinEntityType.ClrType.IsNested || skip.JoinEntityType.ClrType.IsGenericType))
            {
                reasons.Add("Navigation '" + name + "' is a skip/many-to-many relationship without an ordinary explicit CLR join entity; implicit/shared/owned join tables require explicit From joins or a typed script.");
                continue;
            }
            var member = navigation.PropertyInfo;
            var memberName = member?.Name ?? navigation.FieldInfo?.Name;
            var mappedSource = mappedRoots[navigation.DeclaringEntityType];
            if (mappedSource is null && mappedRoots[navigation.TargetEntityType] is not null)
            {
                reasons.Add("Navigation '" + name + "' cannot assign a mapped read row into an entity-shaped navigation. Declare MapInMemoryEntity on both ends or use a typed script.");
                continue;
            }
            if (mappedSource is not null && mappedSource.Navigations?.Any(slot => slot.Name == memberName) != true)
            {
                reasons.Add("Navigation '" + name + "' has no declared plain read-row relationship slot; select mapped/public target stores and HasInMemoryNavigation.");
                continue;
            }
            if (mappedSource is null && (member?.GetMethod?.IsPublic != true || member.SetMethod?.IsPublic != true ||
                member.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit))))
            {
                reasons.Add("Navigation '" + name + "' needs a public readable/writable non-init property for detached loading; backing-field/read-only graphs require a script.");
                continue;
            }
            if (mappedSource is null && navigation.IsCollection && !member!.PropertyType.IsAssignableFrom(typeof(List<>).MakeGenericType(navigation.TargetEntityType.ClrType)))
            {
                reasons.Add("Navigation '" + name + "' must accept a detached List<T> collection; custom collection semantics require a script.");
                continue;
            }
            var assignment = "static (entity, related) => entity." + EscapeIdentifier(memberName!) + " = related" + (navigation.IsCollection ? "" : "!");
            if (navigation is ISkipNavigation many)
            {
                if (CreateInMemoryManyToManyEdge(many, name, variables, mappedRoots, relationshipMetadata, reasons, assignment,
                    expressionList, declaration?.PreserveMissingTargets == true, declaration?.UseMappedPropertyKeys == true, design) is { } edge) edges.Add(edge);
                continue;
            }
            var ordinary = (INavigation)navigation;
            if (!ordinary.IsCollection && (ordinary.IsOnDependent ? ordinary.ForeignKey.IsRequired : ordinary.ForeignKey.IsRequiredDependent) &&
                roots.Single(root => root.EntityTypeName == CSharpTypeName(navigation.TargetEntityType.ClrType)).ModelQueryFilter is not null)
                reasons.Add("Navigation '" + name + "' is a required reference to a filtered target. EF may prune referencing rows through an inner join; fixture root pruning is not inferred. Use a typed script.");
            else if (!ordinary.IsCollection && roots.Single(root => root.EntityTypeName == CSharpTypeName(navigation.TargetEntityType.ClrType)).ModelQueryFilter is not null)
            {
                var safety = new InMemoryFilteredReferenceUsageInspector(ordinary);
                if ((valueExpressions ?? expressionList).Any(expression => !safety.Check(expression)))
                    reasons.Add("Navigation '" + name + "' has a filtered optional target. Use local explicit null guards in scalar/DTO expressions; identity escape, cross-stage assumptions and SQL null propagation require a typed script.");
            }
            var from = ordinary.IsOnDependent ? ordinary.ForeignKey.Properties : ordinary.ForeignKey.PrincipalKey.Properties;
            var to = ordinary.IsOnDependent ? ordinary.ForeignKey.PrincipalKey.Properties : ordinary.ForeignKey.Properties;
            var comparers = new Dictionary<IProperty, InMemoryRelationshipComparerModel>();
            var comparison = InMemoryRelationshipComparison(ordinary.ForeignKey, design, comparers);
            var mappedKeys = declaration?.UseMappedPropertyKeys == true;
            if (!ValidateInMemoryNavigationKeys(name, [(navigation.DeclaringEntityType, from), (navigation.TargetEntityType, to)], mappedRoots, relationshipMetadata, reasons, comparers, mappedKeys)) continue;
            edges.Add(new(name, variables[navigation.DeclaringEntityType], variables[navigation.TargetEntityType], navigation.IsCollection,
                ordinary.IsOnDependent && ordinary.ForeignKey.IsRequired,
                InMemoryNavigationKeysSource(from, navigation.DeclaringEntityType, mappedRoots, mappedKeys),
                InMemoryNavigationKeysSource(to, navigation.TargetEntityType, mappedRoots, mappedKeys), assignment, KeyComparerSource: comparison,
                MappedPropertyKeys: InMemoryMappedRelationshipKeys([("Source", navigation.DeclaringEntityType, from), ("Target", navigation.TargetEntityType, to)], mappedRoots, mappedKeys)));
        }
        return new(roots, edges);
    }
}
