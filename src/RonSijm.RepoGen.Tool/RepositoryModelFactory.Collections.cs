using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static ConfiguredCollectionReconciliationModel[] CreateConfiguredCollections(IEntityType entity,
        RepositoryCommandDesign command, RepositoryGenerationSettingsModel settings, RepositoryDesign? modelDesign,
        List<FilterParameterModel> parameters)
    {
        if (command.Collections.Count == 0)
        {
            return [];
        }

        if (settings.ContractNamespace is null || command.Kind != RepositoryCommandKind.Patch || command.ReloadAfterSave)
        {
            throw new InvalidOperationException("RGD2801: Collection reconciliation requires a closed tracked Patch command, explicit Save/Stage, and no ReloadAfterSave.");
        }

        if (command.ExpectedVersions.Count > 0 && command.Updates.All(update => update.Condition is not null ||
                CommandProperty(entity, update.Property).GetAfterSaveBehavior() != PropertySaveBehavior.Save))
        {
            throw new InvalidOperationException("RGD2801: Collection commands with a parent expected version require an unconditional parent setter so the save checks that version in SQL.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        return command.Collections.Select(collection =>
        {
            var body = collection.Navigation.Body;
            while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
            {
                body = conversion.Operand;
            }

            if (body is not MemberExpression { Expression: ParameterExpression } member || member.Expression != collection.Navigation.Parameters[0] ||
                entity.FindNavigation(member.Member.Name) is not { IsCollection: true, IsOnDependent: false } navigation ||
                !names.Add(navigation.Name))
            {
                throw new InvalidOperationException("RGD2801: ReconcileCollection requires a direct, non-duplicate one-to-many principal navigation; skip/owned relationships are unsupported.");
            }

            var child = navigation.TargetEntityType;
            if (child.IsOwned() || child.HasSharedClrType || child.BaseType is not null || child.GetDerivedTypes().Any() ||
                child.FindPrimaryKey() is null || child.GetNavigations().Any(static value => !value.IsOnDependent) ||
                child.GetReferencingForeignKeys().Any() || child.GetSkipNavigations().Any() || navigation.ForeignKey.IsOwnership)
            {
                throw new InvalidOperationException("RGD2801: Reconciliation supports ordinary keyed leaf children only, not owned/inherited/shared entities or child graphs/cascading descendants.");
            }

            if (collection.MaximumItems < 1 || collection.MaximumItems == int.MaxValue || !Enum.IsDefined(collection.MissingItems))
            {
                throw new InvalidOperationException("RGD2801: Reconciliation requires a positive bounded item limit and a declared missing-item policy.");
            }

            var linkReferences = collection.IsLinkReconciliation ? ValidateLinkReferences(navigation, collection) : null;
            var referenceProperties = linkReferences is null ? [] : linkReferences.Additional.Prepend(linkReferences.Target).SelectMany(static key => key.Properties).ToHashSet();
            var keys = CollectionMatchingProperties(child, navigation, collection);
            var keyNames = keys.Select(static key => key.Name).ToHashSet(StringComparer.Ordinal);

            var itemType = collection.ItemKey.Parameters[0].Type;
            string ItemExpression(LambdaExpression expression, bool hasChild)
            {
                var expected = hasChild ? new[] { child.ClrType, itemType } : new[] { itemType };
                if (expression.Parameters.Count > expected.Length || expression.Parameters.Where((parameter, index) => parameter.Type != expected[index]).Any())
                {
                    throw new InvalidOperationException("RGD2801: Child mappings bind only the child and its declared input item, not additional request parameters.");
                }

                return ProjectionExpressionRenderer.RenderBody(expression, expression.Parameters.Select((_, index) => hasChild && index == 0 ? "child" : "item").ToArray());
            }

            var childDesign = modelDesign?.Entities.FirstOrDefault(definition => definition.EntityType == child.ClrType);
            var protectedWrites = CreateWriteProtection(child, childDesign, settings);
            var allowed = collection.AllowedWrites.Select(value => CommandProperty(child, value).Name).ToHashSet(StringComparer.Ordinal);
            var createAllowed = new HashSet<string>(allowed.Concat(keyNames), StringComparer.Ordinal);
            if (linkReferences is not null) createAllowed.UnionWith(referenceProperties.Select(static property => property.Name));
            string? createSource = null;
            if (collection.CreateMapping is not null)
            {
                if (collection.CreateMapping.Body is not MemberInitExpression { NewExpression.Arguments.Count: 0 } creation || creation.Bindings.Count == 0 ||
                    keyNames.Any(name => !creation.Bindings.Any(binding => binding.Member.Name == name)))
                {
                    throw new InvalidOperationException("RGD2802: Child Add requires an explicit parameterless scalar initializer that supplies every matching key component.");
                }

                if (linkReferences?.Additional.Any(reference => reference.Properties.Any(property => !creation.Bindings.Any(binding => binding.Member.Name == property.Name))) == true)
                    throw new InvalidOperationException("RGD4202: Link Add must explicitly initialize every scalar component of each AdditionalReference; constructor/database defaults are not inferred.");

                foreach (var binding in creation.Bindings)
                {
                    if (binding is not MemberAssignment || child.FindProperty(binding.Member.Name) is not { } property ||
                        property.IsForeignKey() && !referenceProperties.Contains(property) || property.PropertyInfo?.SetMethod?.IsPublic != true)
                    {
                        throw new InvalidOperationException("RGD2802: Child creation must map settable scalars, never ownership/FKs outside a declared link's target key, or navigation graphs.");
                    }

                    ValidateWriteProtection(property, command.MethodName, allowed, protectedWrites);
                    ValidateCommandWrite(property, RepositoryCommandKind.Create, command.MethodName, createAllowed);
                }

                createSource = ItemExpression(collection.CreateMapping, false);
            }

            var updates = collection.Updates.Select(update =>
            {
                var property = CommandProperty(child, update.Property);
                if (keyNames.Contains(property.Name) || property.IsForeignKey() || property.PropertyInfo?.SetMethod?.IsPublic != true)
                {
                    throw new InvalidOperationException("RGD2802: Child updates cannot change matching keys, ownership/FKs, or non-settable properties.");
                }

                ValidateCommandWrite(property, command, allowed, protectedWrites);
                return new ConfiguredUpdateModel("", "", property.Name, ItemExpression(update.Value, true),
                    update.Condition is null ? null : ItemExpression(update.Condition, false));
            }).ToArray();
            if (updates.GroupBy(static update => update.PropertyName, StringComparer.Ordinal).Any(static group => group.Count() > 1))
            {
                throw new InvalidOperationException("RGD2802: A child scalar may only be assigned once per reconciliation.");
            }

            var versions = collection.ExpectedVersions.Select(version =>
            {
                var property = CommandProperty(child, version.Property);
                var usage = new CommandParameterUsage(version.Value.Parameters[0]);
                usage.Visit(version.Value.Body);
                if (!property.IsConcurrencyToken || usage.Used)
                {
                    throw new InvalidOperationException("RGD2802: Child expected versions must reference a mapped concurrency token and come from the input item, never the freshly loaded child.");
                }

                return new ConfiguredVersionModel(property.Name, ItemExpression(version.Value, true));
            }).ToArray();
            if (versions.GroupBy(static version => version.PropertyName, StringComparer.Ordinal).Any(static group => group.Count() > 1))
            {
                throw new InvalidOperationException("RGD2802: A child expected version may only be bound once.");
            }

            if (versions.Length > 0 && updates.All(update => update.ConditionSource is not null ||
                    child.FindProperty(update.PropertyName!)!.GetAfterSaveBehavior() != PropertySaveBehavior.Save))
            {
                throw new InvalidOperationException("RGD2802: Expected child versions require an unconditional child setter so matched rows perform a conditional write.");
            }

            if (collection.Condition is not null && (collection.Condition.Parameters[0].Type != collection.Items.Parameters[0].Type ||
                                                    collection.Condition.Parameters[0].Name != collection.Items.Parameters[0].Name))
            {
                throw new InvalidOperationException("RGD2801: The collection condition must bind the same request type and parameter name as its item source.");
            }

            return new ConfiguredCollectionReconciliationModel(navigation.Name,
                ProjectionExpressionRenderer.RenderBody(collection.Navigation, ["entity"]), CSharpTypeName(child.ClrType), CSharpTypeName(itemType), CSharpTypeName(collection.ChildKey.ReturnType),
                RenderBoundExpression(collection.Items, parameters, skipRow: false),
                CollectionKeySource(ProjectionExpressionRenderer.RenderBody(collection.ChildKey, ["child"]), keys),
                CollectionKeySource(ItemExpression(collection.ItemKey, false), keys),
                collection.Condition is null ? null : RenderBoundExpression(collection.Condition, parameters, skipRow: false),
                createSource, updates, versions, collection.MaximumItems, collection.MissingItems, child.ClrType.FullName!,
                (childDesign?.VisibilityPolicies ?? []).Select(static policy => ProjectionExpressionRenderer.RenderBody(policy.Predicate, ["child"])).ToArray(),
                protectedWrites.Length == 0 ? null : protectedWrites, WritePermissions(protectedWrites, allowed),
                keys.Length == 1 ? null : keys.Select(static key => key.Name).ToArray(),
                linkReferences is null ? null : ConfiguredLinkModel(navigation, linkReferences, collection));
        }).ToArray();
    }
}
