using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static InMemoryQueryModel? CreateInMemoryQuery(IEntityType entity, RepositoryEntityDesign design,
        RepositoryQueryDesign query, RepositoryGenerationSettingsModel settings, IncludeModel[] includes,
        RepositoryDesign? fullDesign, List<FilterParameterModel> parameters, IModel? inMemoryRelationshipMetadata,
        out InMemoryTranslationExpressions? translations)
    {
        translations = null;
        if (!settings.InMemoryTestQueries || design.TestDoubleEnabled == false) return null;
        var translator = fullDesign?.InMemoryTranslations.Count > 0 ? new InMemoryTranslationExpressions(fullDesign.InMemoryTranslations, entity.Model) : null;
        translations = translator;
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        var mappings = new List<(IEntityType Entity, InMemoryEntityMappingModel Mapping)>();
        var modelFilters = new List<(LambdaExpression Expression, InMemoryModelQueryFilterModel Model)>();
        if (query.TestOverrideReason is { } acknowledgement) reasons.Add("Designer requires a test override: " + acknowledgement);
        if (query.Target == RepositoryQueryTarget.Write) reasons.Add("Command-side reads require context/change-tracker semantics; use a typed script.");
        if (query.Source is null)
        {
            if (design.InMemoryStore is not { } store) reasons.Add("No HasInMemoryStore declaration exists for this entity.");
            else if (store.RowType != entity.ClrType)
            {
                if (CreateInMemoryEntityMapping(entity, design, settings, fullDesign) is { } mapping) mappings.Add((entity, mapping));
                else reasons.Add("An explicit data-shape store requires MapInMemoryEntity to adapt scalar query expressions; use a typed script.");
            }
            if (CreateInMemoryModelQueryFilter(entity, design, query, reasons, translator) is { } modelFilter) modelFilters.Add(modelFilter);
            ValidateInMemoryEntityPolicies(entity, query, reasons, modelFilters.Count != 0);
        }
        if (query.RegisteredSource is not null) reasons.Add("Registered provider roots are not portable fixture reads.");
        if (query.Cursor is { OnResults: false } && (query.ResultInputType is not null || query.Group is not null))
            reasons.Add("Entity cursors combined with result transformations require a result-bound cursor or a typed script.");
        if (query.PageConsistency != RepositoryPageConsistency.BestEffort)
            reasons.Add("A relational paging transaction/isolation guarantee is not simulated by fixture snapshots.");

        var source = CreateInMemoryQuerySource(entity.Model, query, fullDesign, parameters, settings, mappings, reasons, modelFilters, out var sourceExpression, translator);
        IEnumerable<Expression> Inventory(bool includeLoading) => RepositoryExpressionInventory.Query(query, includeLoading)
            .Select(expression => expression == query.Source?.Expression && sourceExpression is not null ? sourceExpression : expression)
            .Concat(query.Source is null ? RepositoryExpressionInventory.Root(design, query, includeLoading) : RepositoryExpressionInventory.SourcePolicies(fullDesign, query))
            .Concat(query.Validations.Select(static validation => (Expression)validation.Predicate))
            .Concat(query.ContextKey is null ? [] : new[] { (Expression)query.ContextKey })
            .Concat(modelFilters.Select(static filter => (Expression)filter.Expression))
            .Select(expression => translator?.Visit(expression) ?? expression);
        var expressions = Inventory(includeLoading: true).ToArray();
        var navigation = CreateInMemoryNavigations(entity, query, fullDesign, includes, expressions, source, reasons, inMemoryRelationshipMetadata, settings, mappings, modelFilters, translator,
            Inventory(includeLoading: false).ToArray());
        var inspector = new InMemoryQueryExpressionInspector(reasons);
        foreach (var expression in expressions) inspector.Visit(expression);
        var resultType = ValidateResultPipeline(query, query.RegisteredSource?.ResultType ?? query.Source?.ResultType ?? query.Group?.ResultType ?? query.Projection?.ProjectionType ?? entity.ClrType);
        foreach (var (mappedEntity, mapping) in mappings.DistinctBy(static item => item.Entity))
        {
            bool ContainsModel(Type type) => type == mappedEntity.ClrType || type.HasElementType && ContainsModel(type.GetElementType()!) ||
                type.IsGenericType && type.GetGenericArguments().Any(ContainsModel);
            var terminalTypes = query.Dictionary is { } dictionary ? new[] { dictionary.KeySelector.ReturnType, dictionary.ValueSelector.ReturnType }
                : query.Scalar is { } scalar ? new[] { scalar.Selector.ReturnType } : new[] { resultType };
            if (query.ResultKind is not RepositoryQueryResultKind.Count and not RepositoryQueryResultKind.Exists && terminalTypes.Any(ContainsModel))
                reasons.Add("Mapped source '" + mappedEntity.ClrType.FullName + "' cannot return persistence entity identity through a result or nested collection; project scalar/DTO values instead.");
            var mappingInspector = new InMemoryEntityMappingExpressionInspector(mappedEntity, mapping, reasons);
            // Include selectors name relationships; they do not return entity identity to the consumer.
            // Their mapped keys/slots/loading boundaries are validated by CreateInMemoryNavigations.
            foreach (var expression in Inventory(includeLoading: false)) mappingInspector.Visit(expression);
            if (mappedEntity == entity && query.Source is null && query.DynamicSort is { } sort)
                foreach (var name in sort.PropertyNames)
                    if (!mapping.Members.Any(member => member.Name == name))
                        reasons.Add("Mapped source '" + mappedEntity.ClrType.FullName + "' does not map dynamic sort member '" + name + "'.");
            // Implicit entity projections do not occur in the expression inventory.
            if (mappedEntity == entity && query.Source is null && query.Projection is null && query.Group is null && query.Dictionary is null && query.Scalar is null && query.ResultStages.Count == 0 &&
                query.ResultKind is not RepositoryQueryResultKind.Count and not RepositoryQueryResultKind.Exists)
                reasons.Add("Mapped sources require an explicit scalar/DTO projection instead of returning persistence entities.");
        }

        // A deliberate override is an acknowledgement, not proof of portable execution. Write-side reads
        // are outside the read backend, as are entities explicitly excluded from test-double output.
        if (settings.RequirePortableTestQueries && reasons.Count != 0 && query.TestOverrideReason is null && query.Target == RepositoryQueryTarget.Read)
            throw new InvalidOperationException("RGD3302: Non-portable test query '" + entity.ClrType.FullName + "." + query.MethodName + "': " +
                string.Join(" ", reasons) + " Use RequireTestOverride(reason) to acknowledge a scripted implementation.");
        return new(reasons.Count == 0, reasons.ToArray(), source, navigation, translator?.Receipts.Count > 0 ? translator.Receipts : null,
            ModelQueryFilters: modelFilters.Count == 0 ? null : modelFilters.Select(static filter => filter.Model).ToArray());
    }

    private static void ValidateInMemoryEntityPolicies(IEntityType entity, RepositoryQueryDesign? query, SortedSet<string> reasons, bool modelFilterAccepted = false)
    {
        var prefix = query?.Source is null ? "" : "Source '" + entity.ClrType.FullName + "': ";
        if (!modelFilterAccepted && query?.IgnoreQueryFilters != true && entity.GetAllBaseTypes().Append(entity).Any(static type => type.GetQueryFilter() is not null))
            reasons.Add(prefix + "Active EF model query filters are not lowered into fixture reads. Declare RepoGen visibility policies or use a typed script.");
        if (entity.BaseType is not null || entity.GetDerivedTypes().Any())
            reasons.Add(prefix + "EF inheritance/discriminator semantics are not simulated by entity-shaped fixture stores.");
        // EF deliberately retains convention-owned eager loading even with IgnoreAutoIncludes.
        if (entity.GetNavigations().Any(navigation => navigation.IsEagerLoaded &&
                (query?.IgnoreAutoIncludes != true || navigation.TargetEntityType.IsOwned())) ||
            query?.IgnoreAutoIncludes != true && entity.GetSkipNavigations().Any(static navigation => navigation.IsEagerLoaded))
            reasons.Add(prefix + "EF model AutoInclude navigation loading is not simulated.");
    }

    private static InMemoryQuerySourceModel? CreateInMemoryQuerySource(IModel model, RepositoryQueryDesign query,
        RepositoryDesign? design, List<FilterParameterModel> parameters, RepositoryGenerationSettingsModel settings,
        List<(IEntityType Entity, InMemoryEntityMappingModel Mapping)> mappings, SortedSet<string> reasons,
        List<(LambdaExpression Expression, InMemoryModelQueryFilterModel Model)> modelFilters, out Expression? sourceExpression,
        InMemoryTranslationExpressions? translations = null)
    {
        sourceExpression = null;
        if (query.Source is not { } source) return null;
        var lowerer = new InMemoryQuerySourceLowerer(source.Expression.Parameters[0]);
        Expression body;
        try { body = lowerer.Visit(translations?.Visit(source.Expression.Body) ?? source.Expression.Body)!; }
        catch (InvalidOperationException exception)
        {
            reasons.Add(exception.Message);
            return null;
        }
        catch (ArgumentException)
        {
            reasons.Add("Portable From source contains a typed query construction that cannot be rebound to detached Enumerable roots; use a typed script.");
            return null;
        }
        sourceExpression = Expression.Lambda(body, source.Expression.Parameters);
        var roots = new List<InMemoryQueryRootModel>();
        foreach (var (type, parameter) in lowerer.Roots)
        {
            var entity = model.FindEntityType(type)!; // Production From validation already verifies every Set root.
            var definition = design?.Entities.FirstOrDefault(definition => definition.EntityType == type);
            var selected = definition is not null && definition.RepositoryEnabled && definition.TestDoubleEnabled != false &&
                (definition.ReadEnabled || definition.WriteEnabled) && !type.IsNested && !type.IsGenericType && definition.InMemoryStore is not null;
            if (!selected)
                reasons.Add("Source '" + type.FullName + "' requires HasInMemoryStore on an enabled, selected test-double entity.");
            InMemoryEntityMappingModel? mapping = null;
            if (selected && definition?.InMemoryStore is { } store && store.RowType != type)
            {
                mapping = CreateInMemoryEntityMapping(entity, definition, settings, design);
                if (mapping is null) reasons.Add("Source '" + type.FullName + "' uses an explicit data-shape store without MapInMemoryEntity; scalar source expressions require a typed script.");
                else mappings.Add((entity, mapping));
            }
            var modelFilter = CreateInMemoryModelQueryFilter(entity, definition, query, reasons, translations);
            if (modelFilter is { } accepted) modelFilters.Add(accepted);
            ValidateInMemoryEntityPolicies(entity, query, reasons, modelFilter is not null);
            roots.Add(new(CSharpTypeName(type), parameter.Name!, (definition?.VisibilityPolicies ?? [])
                .Where(policy => !query.BypassedVisibility.Contains(policy.Name, StringComparer.Ordinal))
                .Select(policy => ProjectionExpressionRenderer.Render(translations?.Expand(policy.Predicate) ?? policy.Predicate)).ToArray(),
                definition?.InMemoryStore is { } rootStore ? CSharpTypeName(rootStore.RowType) : null, mapping));
        }
        return new(RenderBoundExpression((LambdaExpression)sourceExpression, [.. parameters], skipRow: true, lambda: false,
                substitutions: lowerer.Roots.Values.ToDictionary(static parameter => parameter, static parameter => parameter.Name!),
                resultNullability: source.ResultType == query.ResultNullability?.Type ? query.ResultNullability : null),
            roots, lowerer.UsedOperators.Order(StringComparer.Ordinal).ToArray());
    }
}
