using System.Text;

namespace RonSijm.RepoGen.Tool;

internal static class CreateManyCommandSourceRenderer
{
    internal static void AppendInputs(StringBuilder builder, ConfiguredCommandModel command, string indent)
    {
        if (command.CreateMany is not { } many) return;
        builder.Append(indent).AppendLine("cancellationToken.ThrowIfCancellationRequested();");
        builder.Append(indent).Append("var __repoGenCreateInput = global::RonSijm.RepoGen.Abstractions.RepositoryCreateInputs.Prepare<")
            .Append(many.ItemTypeName).Append(">((").Append(many.ItemsSource).Append(") ?? throw new global::System.ArgumentNullException(\"items\"), ")
            .Append(many.MaximumItems).AppendLine(", cancellationToken);");
        builder.Append(indent).AppendLine("if (__repoGenCreateInput.Count == 0)");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    return new global::RonSijm.RepoGen.Abstractions.CommandResult")
            .Append(command.ResultValueTypeName is null ? "" : "<" + command.ResultValueTypeName + ">")
            .AppendLine("(global::RonSijm.RepoGen.Abstractions.CommandStatus.NoChanges);");
        builder.Append(indent).AppendLine("}");
    }

    internal static void AppendBody(StringBuilder builder, EntityRepositoryModel entity, ConfiguredCommandModel command,
        string indent, string work, bool returnResult, bool inputsPrepared)
    {
        if (!inputsPrepared) AppendInputs(builder, command, indent);
        builder.Append(indent).Append("var result = await global::RonSijm.RepoGen.EntityFrameworkCore.TrackedCreateManyExecutor.ExecuteAsync<")
            .Append(entity.EntityTypeName).Append(", ").Append(command.CreateMany!.ItemTypeName).Append(", ").Append(command.ResultTypeName ?? "int").AppendLine(">(");
        builder.Append(indent).Append("    context, __repoGenCreateInput, item => ").Append(command.CreateMany.CreateSource).AppendLine(",");
        if (command.SavedResultKeys is not null) SavedBatchResultSourceRenderer.AppendProjection(builder, entity, command, indent);
        else builder.Append(indent).Append("    entity => ").Append(command.ResultSource ?? "0").AppendLine(",");
        builder.Append(indent).Append("    ").Append(command.SaveChanges ? "true" : "false").Append(", ").Append(work).AppendLine(",");
        builder.Append(indent).Append("    new string[] { ").Append(string.Join(", ", entity.CacheInvalidationTags.Select(static tag => "\"" + tag + "\"")))
            .AppendLine(" }, cancellationToken).ConfigureAwait(false);");
        if (returnResult) builder.Append(indent).Append("return result").Append(command.ResultTypeName is null ? ".WithoutValue()" : "").AppendLine(";");
    }
}
