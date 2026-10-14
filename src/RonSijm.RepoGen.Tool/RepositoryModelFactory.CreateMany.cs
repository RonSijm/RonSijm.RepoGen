using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static ConfiguredCreateManyModel? CreateConfiguredCreateMany(IEntityType entity, RepositoryCommandDesign command,
        RepositoryGenerationSettingsModel settings, List<FilterParameterModel> parameters)
    {
        if (command.CreateMany is not { } many) return null;
        if (settings.ContractNamespace is null || command.Kind != RepositoryCommandKind.Create || command.SaveChanges is null ||
            command.CreateMapping is not null || command.Batch is not null || command.Collections.Count > 0 ||
            command.Predicates.Count > 0 || command.Updates.Count > 0 || command.ExpectedVersions.Count > 0 ||
            command.ReturnsMany || command.ReloadAfterSave || command.WithOutcome || command.ConflictKey is not null ||
            many.MaximumItems < 1 || many.MaximumItems == int.MaxValue || entity.IsOwned() || entity.HasSharedClrType ||
            entity.BaseType is not null || entity.GetDerivedTypes().Any())
            throw new InvalidOperationException("RGD3801: CreateMany requires closed ordinary scalar creation, explicit Save/Stage and a positive bounded limit. Existing-row predicates/setters/versions, graph/mixed batches, reload and native result modes are unsupported.");
        if (many.Mapping.Body is not MemberInitExpression { NewExpression.Arguments.Count: 0, Bindings.Count: > 0 } creation ||
            creation.Bindings.Any(binding => binding is not MemberAssignment || entity.FindProperty(binding.Member.Name) is null))
            throw new InvalidOperationException("RGD3802: CreateMany requires an explicit parameterless new entity initializer with mapped scalar assignments only; navigation and graph assignments are unsupported.");
        if (!command.ResultFromSaved && command.ResultProjection is { } result) ValidateBatchScalarExpression(entity, result);
        return new(CSharpTypeName(many.Mapping.Parameters[0].Type), RenderBoundExpression(many.Items, parameters, skipRow: false),
            ProjectionExpressionRenderer.RenderBody(many.Mapping, ["item"]), many.MaximumItems);
    }
}
