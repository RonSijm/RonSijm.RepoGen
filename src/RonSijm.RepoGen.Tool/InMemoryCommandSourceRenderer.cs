using System.Text;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static class InMemoryCommandSourceRenderer
{
    internal static ConfiguredCommandModel? Command(EntityRepositoryModel entity, ScriptedTestDoubleOperationModel operation)
        => entity.Commands.SingleOrDefault(command => RepositoryOperationSignature.Command(command) == operation.Method.Signature);

    internal static string RowType(EntityRepositoryModel entity) => entity.InMemoryStore?.WriteMapping is null ? entity.EntityTypeName : entity.InMemoryStore.EntityMapping!.ReadTypeName;
    internal static string StoreName(EntityRepositoryModel entity) => RepositoryModelFactory.EscapeIdentifier(entity.InMemoryStore?.WriteMapping is null ? entity.EntityName + "Store" : "__RepoGen" + entity.EntityName + "Writes");

    internal static void AppendHandler(StringBuilder builder, RepositoryGenerationModel model, EntityRepositoryModel entity, ScriptedTestDoubleOperationModel operation)
    {
        var command = Command(entity, operation)!;
        var coverage = command.InMemoryCommand!;
        if (coverage.Rendering is { } rendering)
        {
            command = rendering.Command;
            entity = entity with { VisibilityPolicies = rendering.VisibilityPolicies };
        }
        if (coverage.ModelQueryFilter is { } filter && command.Kind != RepositoryCommandKind.Create)
            command = command with { Predicates = [new ConfiguredPredicateModel(filter.PredicateBodySource), .. command.Predicates] };
        builder.Append("        private async ").Append(operation.Method.ReturnType).Append(' ').Append(InMemoryQuerySourceRenderer.HandlerName(operation))
            .Append('(').Append(operation.ArgumentsName).AppendLine(" __repoGenArguments, global::System.Threading.CancellationToken cancellationToken)\n        {");
        void Line(string value) => builder.Append("            ").AppendLine(value);
        Line("cancellationToken.ThrowIfCancellationRequested();");
        var names = ScriptedTestDoubleSourceRenderer.ArgumentPropertyNames(operation);
        for (var index = 0; index < command.Parameters.Count; index++) Line("var " + command.Parameters[index].Name + " = __repoGenArguments." + names[index] + ";");
        ClosedRepositorySourceRenderer.AppendValidations(builder, command.Validations);
        if (command.Batch is not null) InMemoryBatchCommandSourceRenderer.AppendInputs(Line, command);
        if (command.CreateMany is not null) InMemoryCreateManySourceRenderer.AppendInputs(Line, command);
        if (coverage.Collections is { Count: > 0 }) InMemoryCollectionCommandSourceRenderer.AppendInputs(Line, command);
        var boundFilters = coverage.ModelQueryFilter?.Root.ContextBindings is { Count: > 0 } ||
            coverage.Collections?.Any(static collection => collection.State.ModelQueryFilter?.Root.ContextBindings is { Count: > 0 }) == true ||
            coverage.SavedResult?.ModelQueryFilters?.Any(static filter => filter.ContextBindings is { Count: > 0 }) == true;
        if (boundFilters) Line("var " + InMemoryModelFilterContextExpressions.KeyVariable + " = " + command.ContextKeySource + ";");
        Line("var __repoGenData = " + (model.Settings.ContextKeyTypeName is null ? "Fixture.Data" : "Fixture.For(" +
            (boundFilters ? InMemoryModelFilterContextExpressions.KeyVariable : command.ContextKeySource) + ")") + ";");
        if (command.CreateMany is not null)
        {
            InMemoryCreateManySourceRenderer.AppendBody(Line, entity, command);
            builder.AppendLine("        }");
            return;
        }
        if (coverage.IsImmediate)
        {
            if (command.IsAtomic) InMemoryAtomicCommandSourceRenderer.AppendBody(Line, entity, command);
            else InMemoryImmediateCommandSourceRenderer.AppendBody(Line, entity, command);
            builder.AppendLine("        }");
            return;
        }
        if (command.Batch is not null)
        {
            InMemoryBatchCommandSourceRenderer.AppendBody(Line, entity, command);
            builder.AppendLine("        }");
            return;
        }
        var method = coverage.Collections is { Count: > 0 } ? "ReconcileAsync" : command.Kind == RepositoryCommandKind.Create ? "CreateAsync" : command.Kind == RepositoryCommandKind.Patch ? "PatchAsync" : "DeleteAsync";
        Line("var __repoGenResult = await __repoGenData." + method + "<" + RowType(entity) + ", " + entity.InMemoryStore!.KeyTypeName + ", " + (command.ResultTypeName ?? "int") + ">(");
        Line("    __repoGenData." + StoreName(entity) + ",");
        if (command.Kind == RepositoryCommandKind.Create) Line("    () => " + (coverage.MappedCreateSource ?? command.CreateSource) + ",");
        else
        {
            Line("    entity => " + (command.Predicates.Count == 0 ? "true" : string.Join(" && ", command.Predicates.Select(static predicate => "(" + predicate.PredicateSource + ")"))) + ",");
            if (command.Kind == RepositoryCommandKind.Patch)
            {
                Line("    entity =>");
                Line("    {");
                Line("        var __repoGenAssigned = new global::System.Collections.Generic.List<string>();");
                foreach (var update in command.Updates)
                {
                    if (update.ConditionSource is not null) Line("        if (" + update.ConditionSource + ")");
                    Line("        {");
                    Line("            entity." + InMemoryWriteState.Access(update.PropertyName!, coverage.MappedPropertyState) + " = " + update.ValueBodySource + ";");
                    Line("            __repoGenAssigned.Add(" + System.Text.Json.JsonSerializer.Serialize(update.PropertyName) + ");");
                    Line("        }");
                }
                Line("        return __repoGenAssigned;");
                Line("    },");
                if (coverage.Collections is { Count: > 0 }) InMemoryCollectionCommandSourceRenderer.AppendPlans(Line, entity, command);
            }
            Line("    entity => " + (command.ExpectedVersions is not { Count: > 0 } ? "true" : string.Join(" && ", command.ExpectedVersions.Select(version =>
                InMemoryValueEqualitySourceRenderer.Compare(coverage.ValueEqualities, version.PropertyName,
                    "entity." + InMemoryWriteState.Access(version.PropertyName, coverage.MappedPropertyState), version.ValueSource)))) + ",");
            if (command.Kind == RepositoryCommandKind.Patch)
            {
                Line("    " + coverage.SameValuesSource + ",");
                Line("    " + coverage.SameTokensSource + ",");
                AppendMerge(Line, coverage);
            }
            else Line("    " + coverage.SameTokensSource + ",");
        }
        if (coverage.SavedResult is not null) InMemorySavedResultSourceRenderer.AppendResult(Line, entity, command);
        else Line("    entity => " + (command.ResultSource ?? "0") + ",");
        Line("    save: " + (command.SaveChanges ? "true" : "false") + ",");
        if (command.Kind == RepositoryCommandKind.Patch) Line("    forceWrite: " + (command.ExpectedVersions is { Count: > 0 } ? "true" : "false") + ",");
        if (command.Kind == RepositoryCommandKind.Patch && coverage.ValueEqualities is { Count: > 0 } equalities)
            Line("    sameAssignedValue: " + InMemoryValueEqualitySourceRenderer.Assigned(equalities, coverage.MappedPropertyState) + ",");
        Line("    cancellationToken: cancellationToken).ConfigureAwait(false);");
        Line("return __repoGenResult" + (command.ResultTypeName is null ? ".WithoutValue()" : "") + ";");
        builder.AppendLine("        }");
    }

    internal static void AppendMerge(Action<string> line, InMemoryCommandModel coverage)
    {
        line("    static (__repoGenTarget, __repoGenSource, __repoGenAssigned) =>");
        line("    {");
        line("        foreach (var __repoGenProperty in __repoGenAssigned)");
        line("            switch (__repoGenProperty)");
        line("            {");
        foreach (var property in coverage.MergeProperties)
            line("                case " + System.Text.Json.JsonSerializer.Serialize(property) + ": __repoGenTarget." + InMemoryWriteState.Access(property, coverage.MappedPropertyState) + " = __repoGenSource." + InMemoryWriteState.Access(property, coverage.MappedPropertyState) + "; break;");
        line("                default: throw new global::System.InvalidOperationException(\"Unknown approved fixture write member.\");");
        line("            }");
        line("    },");
    }
}
