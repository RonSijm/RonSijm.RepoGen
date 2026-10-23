using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static InMemoryCommandModel? CreateInMemoryCommand(IEntityType entity, RepositoryEntityDesign definition,
        RepositoryCommandDesign command, RepositoryGenerationSettingsModel settings, IModel? metadata, RepositoryDesign? fullDesign, List<FilterParameterModel> parameters,
        out InMemoryTranslationExpressions? translations)
    {
        translations = null;
        if (!settings.InMemoryTestCommands || definition.TestDoubleEnabled == false) return null;
        var read = definition.InMemoryStore?.WriteMapping is null ? null : CreateInMemoryEntityMapping(entity, definition, settings, fullDesign);
        var writeMapping = CreateInMemoryWriteMapping(entity, definition, settings, read);
        var mapped = writeMapping is not null;
        var state = writeMapping?.MappedPropertyState;
        var atomic = command.Kind is RepositoryCommandKind.InsertIfAbsent or RepositoryCommandKind.InsertOrUpdate;
        // Production capture permission is not fixture state. Only a complete, separately opted-in
        // public-data write map can supply returned scalars without materializing EF/native buffers.
        var returningState = mapped && definition.InMemoryStore?.MappedPropertyStateEnabled == true &&
            (command.ReturnsMany || atomic && command.ResultProjection is not null) && command.InMemoryImmediateWrites &&
            (command.IncludeReturningShadowProperties || command.IncludeReturningMappedProperties)
                ? new InMemoryReturningStateModel(command.IncludeReturningShadowProperties, command.IncludeReturningMappedProperties) : null;
        var declarations = command.InMemoryTranslations ? fullDesign?.InMemoryTranslations : null;
        var propertyBindings = CreateInMemoryCommandPropertyBindings(entity, state, command, settings, fullDesign);
        var translator = declarations?.Count > 0 || propertyBindings is not null || returningState is not null
            ? new InMemoryTranslationExpressions(declarations ?? [], entity.Model,
                propertyBindings: propertyBindings) : null;
        translations = translator;
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        if (command.IncludeReturningMappedProperties && returningState is null)
            reasons.Add("IncludeMappedProperties needs native mapped scalar values; fixture field/indexer/shadow state is not simulated without complete MapInMemoryWrites(useMappedPropertyState: true) and SimulateImmediateWrites. Use explicit scalar state, a typed override or a real-provider fixture.");
        if (command.IncludeReturningShadowProperties && returningState is null)
            reasons.Add("IncludeShadowProperties needs native returned property values; fixture shadow state is not simulated without complete MapInMemoryWrites(useMappedPropertyState: true) and SimulateImmediateWrites. Use explicit scalar state, a typed override or a real-provider fixture.");
        var modelFilter = CreateInMemoryModelQueryFilter(entity, command.InMemoryModelQueryFilterEnabled, reasons, translator, fullDesign);
        var creates = command.Kind == RepositoryCommandKind.Create || atomic;
        var immediate = command.InMemoryImmediateWrites && (atomic || command.Kind is RepositoryCommandKind.Update or RepositoryCommandKind.Delete);
        if (command.TestOverrideReason is { } reason) reasons.Add("Designer requires a test override: " + reason);
        if (command.InMemoryImmediateWrites && !immediate)
            reasons.Add("SimulateImmediateWrites supports immediate scalar Update/Delete/InsertIfAbsent/InsertOrUpdate only, not tracked or graph commands.");
        if (!immediate && command.Kind is not RepositoryCommandKind.Create and not RepositoryCommandKind.Patch and not RepositoryCommandKind.TrackedDelete)
            reasons.Add("Only designed tracked Create/Patch/Delete commands are simulated by default; immediate scalar Update/Delete/InsertIfAbsent/InsertOrUpdate need explicit SimulateImmediateWrites.");
        if (command.Collections.Count != 0 && !command.InMemoryCollectionWrites)
            reasons.Add("Graph reconciliation writes require a typed script or a real-provider fixture.");
        if (command.InMemoryCollectionWrites && (command.Kind != RepositoryCommandKind.Patch || command.Collections.Count == 0))
            reasons.Add("SimulateCollectionWrites requires a designed tracked Patch with bounded ReconcileCollection or ReconcileLinks rules.");
        if (command.Kind is RepositoryCommandKind.TrackedDelete or RepositoryCommandKind.Delete && entity.GetReferencingForeignKeys().Any())
            reasons.Add("Deleting rows with dependent relationships requires a typed script or real-provider fixture; cascades/fixup are not simulated.");
        if (command.ReloadAfterSave) reasons.Add("ReloadAfterSave/provider-generated values are not simulated.");
        if (definition.InMemoryStore is not { } store || !mapped && (store.RowType != entity.ClrType || !entity.ClrType.IsVisible))
            reasons.Add("Portable writes require a selected public entity-shaped HasInMemoryStore or explicit MapInMemoryEntity and MapInMemoryWrites; public proxy writes are not inferred.");
        if (entity.IsOwned() || entity.HasSharedClrType || entity.BaseType is not null || entity.GetDerivedTypes().Any())
            reasons.Add("Owned/shared/inherited write shapes are not simulated.");
        if (entity.GetQueryFilter() is not null && modelFilter is null || entity.GetNavigations().Any(static navigation => navigation.IsEagerLoaded) ||
            entity.GetSkipNavigations().Any(static navigation => navigation.IsEagerLoaded))
            reasons.Add("Active EF filters/AutoInclude write semantics require an override; declare portable designer visibility instead.");
        if (modelFilter is not null && command.Collections.Count != 0 && !command.InMemoryCollectionModelQueryFilters)
            reasons.Add("Opted-in root model filters need SimulateCollectionWrites(includeModelQueryFilters: true) for designed collection writes; other filtered graphs require an override.");
        var properties = entity.GetProperties().OrderBy(static property => property.Name, StringComparer.Ordinal).ToArray();
        if (command.ExpectedVersions.Concat(command.Batch?.ExpectedVersions ?? []).Any(version => version.MappedPropertyName is { } name &&
                entity.FindProperty(name)?.PropertyInfo?.GetMethod?.IsPublic != true && state?.Any(binding => binding.PropertyName == name) != true))
            reasons.Add("Named concurrency tokens without public CLR members require native/tracked EF state; fixture shadow/backing-field tokens need a typed override.");
        var equalities = CreateInMemoryValueEqualities(entity, definition, settings);
        if (!mapped && properties.Any(static property => property.PropertyInfo?.GetMethod?.IsPublic != true || !property.ClrType.IsVisible))
            reasons.Add("Write state comparison needs public readable CLR scalar properties; shadow/backing-field state is not simulated.");
        if (properties.Any(static property => !PortableWriteScalar(property.ClrType)))
            reasons.Add("Portable write state supports ordinary primitive/enum/string/decimal/Guid/date/time/binary scalars, not arbitrary mapped object or collection behavior.");
        if (properties.Any(property => (creates && (property.ValueGenerated & ValueGenerated.OnAdd) != 0 ||
            command.Kind is RepositoryCommandKind.Patch or RepositoryCommandKind.Update or RepositoryCommandKind.InsertOrUpdate && (property.ValueGenerated & ValueGenerated.OnUpdate) != 0) &&
            !HasInMemoryValueAdapter(property, definition.InMemoryStore, metadata)))
            reasons.Add("Create stores require caller-supplied keys/values; generated values on save/update require an explicit test adapter and are not inferred.");
        if (properties.Any(property => InMemoryRelationshipMetadata.RequiresCustomEquality(property, metadata) &&
            (immediate || !HasExplicitInMemoryEquality(property, equalities, metadata))))
            reasons.Add("Custom scalar conversions/comparers require explicit test behavior rather than ordinary CLR/structural comparison.");
        var expressions = RepositoryExpressionInventory.Command(command, includeCollections: !command.InMemoryCollectionWrites, includeResult: !command.ResultFromSaved).Concat(command.Kind == RepositoryCommandKind.Create ? [] :
                definition.VisibilityPolicies.Where(policy => !command.BypassedVisibility.Contains(policy.Name)).Select(static policy => (Expression)policy.Predicate))
            .Concat(command.Validations.Select(static validation => (Expression)validation.Predicate))
            .Concat(command.ContextKey is null ? [] : new[] { (Expression)command.ContextKey }).ToArray();
        if (modelFilter is { } accepted) expressions = [.. expressions, accepted.Expression];
        var inspector = new InMemoryQueryExpressionInspector(reasons);
        var createMapping = command.CreateMany?.Mapping ?? command.CreateMapping;
        foreach (var expression in expressions)
        {
            if (mapped && expression == createMapping && createMapping?.Body is MemberInitExpression creation)
                foreach (var assignment in creation.Bindings.OfType<MemberAssignment>()) inspector.Visit(translator?.Visit(assignment.Expression) ?? assignment.Expression);
            else inspector.Visit(translator?.Visit(expression) ?? expression);
            if (RepositoryQuerySourceRenderer.NavigationReferences(entity.Model, translator?.Visit(expression) ?? expression).Count != 0)
                reasons.Add("Navigation-dependent write selection/mapping requires a typed script; detached graph loading is a read capability only.");
        }
        if (mapped)
        {
            var scalarInspector = new InMemoryEntityMappingExpressionInspector(entity, read!, reasons);
            var valueInspector = new InMemoryMappingValueExpressionInspector(entity.Model, reasons, entity.ClrType, allowScalarSequences: true);
            foreach (var expression in expressions)
                if (expression == createMapping && createMapping?.Body is MemberInitExpression creation)
                    foreach (var assignment in creation.Bindings.OfType<MemberAssignment>())
                    { var value = translator?.Visit(assignment.Expression) ?? assignment.Expression; scalarInspector.Visit(value); valueInspector.Visit(value); }
                else { var value = translator?.Visit(expression) ?? expression; scalarInspector.Visit(value); valueInspector.Visit(value); }
        }
        var key = definition.InMemoryStore?.KeySelector;
        IEnumerable<Expression> keyParts = key?.Body is NewExpression compound ? compound.Arguments : key is null ? [] : new[] { key.Body };
        var names = keyParts.Select(part => part is MemberExpression { Expression: ParameterExpression parameter } member && parameter == key!.Parameters[0] ? member.Member.Name : "").ToArray();
        if (!mapped && !names.SequenceEqual(entity.FindPrimaryKey()?.Properties.Select(static property => property.Name) ?? []))
            reasons.Add("The write fixture key must directly select the complete EF primary key in declared order.");
        var merges = properties.Where(property => !property.IsKey() && (mapped || property.PropertyInfo?.SetMethod?.IsPublic == true &&
                !property.PropertyInfo.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit))))
            .Select(static property => property.Name).ToArray();
        if (command.Updates.Concat(command.Batch?.Updates ?? []).Any(update => !merges.Contains(update.MappedPropertyName ??
                (update.Property?.Body as MemberExpression)?.Member.Name ?? "", StringComparer.Ordinal)))
            reasons.Add("Approved patch members need public non-init setters and cannot change keys.");
        if (command.Updates.Concat(command.Batch?.Updates ?? []).Any(update => update.MappedPropertyName is { } name &&
                entity.FindProperty(name) is { } property &&
                (property.PropertyInfo?.GetMethod?.IsPublic != true || property.PropertyInfo.SetMethod?.IsPublic != true || property.IsIndexerProperty()) &&
                state?.Any(binding => binding.PropertyName == name) != true))
            reasons.Add("Named non-public/shadow/indexer setters require complete MapInMemoryWrites(useMappedPropertyState: true) and exact public-data property bindings; production entry access is not fixture state.");
        // Native INSERT maps columns, not CLR constructor/property defaults. Require complete scalar
        // inputs or explicit generation and lower both entity-shaped and mapped candidates identically.
        var mappedCreate = creates && (mapped || immediate && atomic)
            ? CreateInMemoryScalarCreate(entity, definition, command, mapped ? read!.ReadTypeName : CSharpTypeName(entity.ClrType), parameters, reasons, translator, metadata, state) : null;
        if (immediate && atomic && !mapped && properties.Any(static property => property.PropertyInfo?.SetMethod?.IsPublic != true ||
            property.PropertyInfo.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit))))
            reasons.Add("Immediate insert candidates require public non-init scalar setters or complete public write maps.");
        var immediateAssignments = immediate ? CreateInMemoryImmediateAssignments(entity, command, parameters, reasons, translator) : null;
        var collections = command.InMemoryCollectionWrites && command.Collections.Count != 0
            ? CreateInMemoryCollections(entity, command, settings, metadata, fullDesign, reasons, translator) : null;
        var savedResult = CreateInMemorySavedResult(entity, definition, command, settings, metadata, fullDesign, read, reasons, translator);
        if (settings.RequirePortableTestCommands && reasons.Count != 0 && command.TestOverrideReason is null)
            throw new InvalidOperationException("RGD3402: Non-portable test command '" + entity.ClrType.FullName + "." + command.MethodName + "': " +
                string.Join(" ", reasons) + " Use RequireTestOverride(reason) to acknowledge a scripted implementation.");
        return new(reasons.Count == 0, reasons.ToArray(), InMemorySameScalars(properties, equalities, state), InMemorySameScalars(properties.Where(static property => property.IsConcurrencyToken), equalities, state), merges, mappedCreate, immediateAssignments, collections, savedResult,
            translator?.Receipts.Count > 0 ? translator.Receipts : null, ValueEqualities: equalities,
            ModelQueryFilter: modelFilter is { } filter ? new(filter.Model, ProjectionExpressionRenderer.RenderBody(filter.Expression,
                filter.Expression.Parameters.Count == 1 ? ["entity"] : ["entity", InMemoryModelFilterContextExpressions.KeyVariable])) : null,
            MappedPropertyState: state, ReturningState: returningState);
    }

    private static InMemoryImmediateAssignmentModel[] CreateInMemoryImmediateAssignments(IEntityType entity,
        RepositoryCommandDesign command, List<FilterParameterModel> parameters, SortedSet<string> reasons, InMemoryTranslationExpressions? translations = null)
    {
        var assignments = new List<InMemoryImmediateAssignmentModel>();
        if (command.Kind == RepositoryCommandKind.Delete && command.Updates.Count != 0)
            reasons.Add("Immediate fixture Delete cannot also configure update setters.");
        foreach (var update in command.Updates)
        {
            var property = update.MappedPropertyName is { } name ? entity.FindProperty(name)
                : update.Property?.Body is MemberExpression { Expression: ParameterExpression } member && member.Expression == update.Property.Parameters[0]
                    ? entity.FindProperty(member.Member.Name) : null;
            if (property is null || property.IsKey() || property.ValueGenerated != ValueGenerated.Never || property.GetAfterSaveBehavior() != PropertySaveBehavior.Save)
            {
                reasons.Add("Immediate fixture setters must select ordinary writable non-key, non-generated mapped scalars directly.");
                continue;
            }
            // Keep CLR designer values, not a native writer's provider-column conversion.
            assignments.Add(new(property.Name, RenderBoundExpression(translations?.Expand(update.Value) ?? update.Value, parameters, skipRow: true, lambda: false)));
        }
        if (assignments.GroupBy(static assignment => assignment.PropertyName, StringComparer.Ordinal).Any(static group => group.Count() > 1))
            reasons.Add("Immediate fixture commands cannot assign the same scalar more than once.");
        return assignments.ToArray();
    }

    private static bool PortableWriteScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) ||
            type == typeof(TimeSpan) || type == typeof(byte[]);
    }
}
