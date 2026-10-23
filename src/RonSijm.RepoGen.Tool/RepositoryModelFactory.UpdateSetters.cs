using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static ConfiguredUpdateModel[] CreateConfiguredUpdates(IEntityType entity, RepositoryCommandDesign command,
        List<FilterParameterModel> parameters, HashSet<string> allowed, string[] protectedWrites, string[] conflictColumns)
    {
        var tracked = command.Kind is RepositoryCommandKind.Create or RepositoryCommandKind.Patch or RepositoryCommandKind.TrackedDelete;
        var atomic = command.Kind is RepositoryCommandKind.InsertIfAbsent or RepositoryCommandKind.InsertOrUpdate;
        var returning = command.ReturnsMany;
        return command.Updates.Select(update =>
        {
            var names = new string[update.Value.Parameters.Count];
            names[0] = "entity";
            foreach (var (parameter, index) in update.Value.Parameters.Skip(1).Select((parameter, index) => (parameter, index)))
            {
                names[index + 1] = ToParameterName(parameter.Name ?? "value" + (index + 1));
                AddParameter(parameters, names[index + 1], parameter.Type);
            }

            var mapped = update.MappedPropertyName is not null;
            var property = mapped || tracked || atomic || returning || protectedWrites.Length > 0 ? UpdateProperty(entity, update) : null;
            if (mapped)
            {
                ValidateMappedUpdateTarget(entity, property!, update);
            }
            if (atomic && property is not null && (property.IsKey() || conflictColumns.Contains(property.Name, StringComparer.Ordinal)))
                throw new InvalidOperationException("Atomic updates cannot change a key or declared conflict target.");
            if (property is not null) ValidateCommandWrite(property, command, allowed, protectedWrites);
            if (returning && !global::RonSijm.RepoGen.Internal.ReturningPropertyAccess.Ordinary(property!))
                throw new InvalidOperationException("RGD1306: Returning update targets must remain ordinary CLR properties with getters/setters; capturing mapped values does not permit field/indexer assignments.");
            if (atomic && (command.IncludeReturningShadowProperties || command.IncludeReturningMappedProperties) && !global::RonSijm.RepoGen.Internal.ReturningPropertyAccess.Ordinary(property!))
                throw new InvalidOperationException("RGD1306: Atomic update targets must remain ordinary CLR properties with getters/setters; capture permission does not enable field/indexer assignments.");
            if (returning && (property!.ValueGenerated != ValueGenerated.Never || property.GetAfterSaveBehavior() != PropertySaveBehavior.Save))
                throw new InvalidOperationException($"RGD1306: Returning updates cannot assign generated column '{property.Name}'.");
            if (update.Condition is not null && command.Kind != RepositoryCommandKind.Patch)
                throw new InvalidOperationException("Conditional assignments are supported by tracked Patch() commands only.");
            if (returning && update.Value.ReturnType != property!.ClrType)
                throw new InvalidOperationException("RGD1306: Returning setters must use the mapped scalar CLR type without widening or boxing.");

            var sourceValue = returning ? ReturningValue(property!, update.Value) : update.Value;
            string body;
            if (tracked && mapped)
            {
                if (command.Collections.Count != 0 && RepositoryQuerySourceRenderer.NavigationReferences(entity.Model, sourceValue).Count != 0)
                    throw new InvalidOperationException("RGD1312: Tracked mapped reconciliation parent values read this parent's scalars, not navigation graphs.");
                body = RenderTrackedMappedValue(entity, sourceValue, names);
            }
            else body = ProjectionExpressionRenderer.RenderBody(sourceValue, names);
            var expressionBody = ProjectionExpressionRenderer.RenderBody(update.Value, names);
            if (mapped && !property!.ClrType.IsValueType && !property.IsNullable)
            {
                // Expression trees erase nullable-reference/null-forgiving annotations. The model defines
                // the generated type; runtime input/constraint validation retains its normal boundary.
                body = "(" + body + "!)";
                expressionBody = "(" + expressionBody + "!)";
            }
            return new ConfiguredUpdateModel(mapped ? "entity => " + MappedPropertyRead(property!) : ProjectionExpressionRenderer.Render(update.Property!),
                "entity => " + expressionBody, property?.Name, body,
                update.Condition is null ? null : RenderBoundExpression(update.Condition, parameters, skipRow: false),
                returning ? CSharpTypeName(sourceValue.ReturnType) + (sourceValue.ReturnType.IsValueType ? "" : "?") : null,
                MappedProperty: mapped ? new MappedPropertyBindingModel(property!.Name, CSharpTypeName(property.ClrType, property.IsNullable), property.IsShadowProperty()) : null);
        }).ToArray();
    }

    private static IProperty UpdateProperty(IEntityType entity, RepositoryUpdateDesign update) => update.MappedPropertyName is { } name
        ? MappedProperty(entity, name, update.PropertyType, "RGD1311") : CommandProperty(entity, update.Property!);

    private static string RenderTrackedMappedValue(IEntityType entity, LambdaExpression value, string[] names)
    {
        var reads = new TrackedMappedValueRewriter(entity, value.Parameters[0], names[0]);
        return ProjectionExpressionRenderer.RenderBody(Expression.Lambda(reads.Visit(value.Body)!, value.Parameters), names, reads.Substitutions);
    }

    private static void ValidateMappedUpdateTarget(IEntityType entity, IProperty property, RepositoryUpdateDesign update)
    {
        if (entity.IsOwned() || entity.HasSharedClrType || entity.BaseType is not null || entity.GetDerivedTypes().Any() ||
            property.IsKey() || property.IsForeignKey() || property.GetContainingIndexes().Any(static index => index.IsUnique) ||
            property.ValueGenerated != ValueGenerated.Never || property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save ||
            property.GetAfterSaveBehavior() != PropertySaveBehavior.Save)
            throw new InvalidOperationException("RGD1311: Named mapped setters require an ordinary writable non-generated scalar; keys, foreign keys, unique identity, owned/shared/inherited shapes and read-only columns are excluded even with AllowWrite.");
        if (update.Value.ReturnType != property.ClrType)
            throw new InvalidOperationException("RGD1311: A named mapped setter must use the exact model CLR type without boxing or widening.");
    }

    private static HashSet<string> CommandWritePermissions(IEntityType entity, RepositoryCommandDesign command) =>
        CommandWritePermissions(entity, command.AllowedWrites, command.AllowedMappedWrites);

    private static HashSet<string> CommandWritePermissions(IEntityType entity, IReadOnlyList<LambdaExpression> members,
        IReadOnlyDictionary<string, Type> mapped) =>
        members.Select(property => CommandProperty(entity, property).Name)
            .Concat(mapped.Select(binding => MappedProperty(entity, binding.Key, binding.Value, "RGD1311").Name))
            .ToHashSet(StringComparer.Ordinal);

    private static void ValidateMappedUpdateLifecycle(RepositoryCommandDesign command)
    {
        if (command.Updates.Any(static update => update.MappedPropertyName is not null) &&
            (command.Kind is not RepositoryCommandKind.Patch and not RepositoryCommandKind.Update || command.ReturnsMany || command.Batch is not null ||
                command.Collections.Count != 0 && command.Kind != RepositoryCommandKind.Patch))
            throw new InvalidOperationException("RGD1311: Named root setters require tracked Patch (optionally bounded reconciliation) or ordinary set-based Update. Batches use item setters; native returning, atomic and broader graph targets remain separate capabilities.");
    }
}
