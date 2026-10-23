using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static ConfiguredBatchCommandModel? CreateConfiguredBatch(IEntityType entity, RepositoryCommandDesign command,
        RepositoryGenerationSettingsModel settings, List<FilterParameterModel> parameters, HashSet<string> allowed, string[] protectedWrites)
    {
        if (command.Batch is not { } batch)
        {
            return null;
        }
        if (settings.ContractNamespace is null || command.Kind is not RepositoryCommandKind.Patch and not RepositoryCommandKind.TrackedDelete ||
            command.SaveChanges is null || command.CreateMapping is not null || command.Collections.Count > 0 || command.Updates.Count > 0 ||
            command.ExpectedVersions.Count > 0 || command.ReturnsMany || command.ReloadAfterSave || command.WithOutcome ||
            batch.MaximumItems < 1 || batch.MaximumItems == int.MaxValue)
        {
            throw new InvalidOperationException("RGD3001: Batches require closed PatchMany/DeleteMany, an explicit Save/Stage and a positive bounded limit. Configure setters/versions on the item builder; graph mappings, ReturnsMany, reload and other command shapes are unsupported.");
        }

        var key = BatchProperty(entity, batch.EntityKey, "RGD3001");
        var unique = entity.GetKeys().Any(candidate => candidate.Properties.Count == 1 && candidate.Properties[0] == key) ||
            entity.GetIndexes().Any(candidate => candidate.IsUnique && candidate.GetFilter() is null && candidate.Properties.Count == 1 && candidate.Properties[0] == key);
        if (key.IsNullable || key.ClrType.IsArray || key.ClrType != batch.EntityKey.ReturnType || key.ClrType != batch.ItemKey.ReturnType ||
            key.PropertyInfo?.GetMethod?.IsPublic != true || !unique || entity.IsOwned() || entity.HasSharedClrType ||
            entity.BaseType is not null || entity.GetDerivedTypes().Any())
        {
            throw new InvalidOperationException("RGD3001: Batch matching requires one non-null mapped CLR scalar with a declared primary/alternate key or unfiltered unique index on an ordinary entity. Composite, array, owned/shared and inherited shapes are unsupported.");
        }

        if (command.Kind == RepositoryCommandKind.TrackedDelete && (batch.Updates.Count > 0 || entity.GetReferencingForeignKeys().Any()))
        {
            throw new InvalidOperationException("RGD3001: DeleteMany supports leaf rows without cascading dependents and cannot configure setters. Use an explicit graph adapter for relationship deletion.");
        }

        var itemType = batch.ItemKey.Parameters[0].Type;
        string ItemExpression(LambdaExpression expression, bool hasEntity, bool mappedReads = false)
        {
            var expected = hasEntity ? new[] { entity.ClrType, itemType } : new[] { itemType };
            if (expression.Parameters.Count > expected.Length || expression.Parameters.Where((parameter, index) => parameter.Type != expected[index]).Any())
            {
                throw new InvalidOperationException("RGD3002: Batch item mappings bind only the entity and its declared item type.");
            }

            ValidateBatchScalarExpression(entity, expression);
            var names = expression.Parameters.Select((_, index) => hasEntity && index == 0 ? "entity" : "item").ToArray();
            return mappedReads ? RenderTrackedMappedValue(entity, expression, names) : ProjectionExpressionRenderer.RenderBody(expression, names);
        }

        var updates = batch.Updates.Select(update =>
        {
            var mapped = update.MappedPropertyName is not null;
            var property = mapped ? UpdateProperty(entity, update) : BatchProperty(entity, update.Property!, "RGD3002");
            if (mapped) ValidateMappedUpdateTarget(entity, property, update);
            if (property.Name == key.Name || !mapped && (property.PropertyInfo?.SetMethod?.IsPublic != true || property.GetAfterSaveBehavior() != PropertySaveBehavior.Save))
            {
                throw new InvalidOperationException("RGD3002: Batch setters cannot change their matching key or a non-settable/read-only scalar.");
            }

            ValidateCommandWrite(property, command, allowed, protectedWrites);
            // Preserve the established member-based path. Only explicit named assignments lower
            // scalar current-row reads through EF entry state (including field/indexer access modes).
            var body = ItemExpression(update.Value, true, mappedReads: mapped);
            if (mapped && !property.ClrType.IsValueType && !property.IsNullable) body = "(" + body + "!)";
            return new ConfiguredUpdateModel("", "", property.Name, body,
                update.Condition is null ? null : ItemExpression(update.Condition, false),
                MappedProperty: mapped ? new MappedPropertyBindingModel(property.Name, CSharpTypeName(property.ClrType, property.IsNullable), property.IsShadowProperty()) : null);
        }).ToArray();
        if (command.Kind == RepositoryCommandKind.Patch && updates.Length == 0 ||
            updates.GroupBy(static update => update.PropertyName, StringComparer.Ordinal).Any(static group => group.Count() > 1))
        {
            throw new InvalidOperationException("RGD3002: PatchMany requires at least one explicit setter, with no repeated scalar assignments.");
        }

        var versions = batch.ExpectedVersions.Select(version =>
        {
            var property = version.MappedPropertyName is null ? BatchProperty(entity, version.Property!, "RGD3002") : VersionProperty(entity, version, "RGD1310");
            var usage = new CommandParameterUsage(version.Value.Parameters[0]);
            usage.Visit(version.Value.Body);
            if (!property.IsConcurrencyToken || usage.Used)
            {
                throw new InvalidOperationException("RGD3002: Batch expected versions must reference concurrency tokens and come from each item, never a freshly loaded entity.");
            }

            return new ConfiguredVersionModel(property.Name, ItemExpression(version.Value, true), version.MappedPropertyName is null ? null : PropertyBinding(property));
        }).ToArray();
        if (versions.GroupBy(static version => version.PropertyName, StringComparer.Ordinal).Any(static group => group.Count() > 1) ||
            command.Kind == RepositoryCommandKind.Patch && versions.Length > 0 && updates.All(static update => update.ConditionSource is not null))
        {
            throw new InvalidOperationException("RGD3002: Bind each token only once. Expected-version patches require an unconditional setter so every matched item performs a conditional SQL write.");
        }

        if (!command.ResultFromSaved && command.ResultProjection is { } result)
        {
            ValidateBatchScalarExpression(entity, result);
        }
        return new(CSharpTypeName(itemType), CSharpTypeName(key.ClrType), RenderBoundExpression(batch.Items, parameters, skipRow: false),
            ProjectionExpressionRenderer.RenderBody(batch.EntityKey, ["entity"]), ItemExpression(batch.ItemKey, false), key.Name,
            batch.MaximumItems, updates, versions);
    }

    private static void ValidateBatchScalarExpression(IEntityType entity, LambdaExpression expression)
    {
        var visitor = new BatchScalarExpressionVisitor(entity);
        visitor.Visit(expression);
        if (visitor.HasNavigation)
        {
            throw new InvalidOperationException("RGD3002: Batch setters/results read mapped scalars, not navigation graphs. Configure graph reads separately.");
        }
    }

    private static IProperty BatchProperty(IEntityType entity, LambdaExpression selector, string diagnostic)
    {
        try
        {
            return CommandProperty(entity, selector);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(diagnostic + ": Batch selectors require direct mapped scalar properties. " + exception.Message, exception);
        }
    }

    private sealed class BatchScalarExpressionVisitor(IEntityType entity) : ExpressionVisitor
    {
        internal bool HasNavigation { get; private set; }
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression?.Type == entity.ClrType && (entity.FindNavigation(node.Member.Name) is not null || entity.FindSkipNavigation(node.Member.Name) is not null))
            {
                HasNavigation = true;
            }

            return base.VisitMember(node);
        }
    }
}
