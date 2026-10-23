using System.Text.Json;

namespace RonSijm.RepoGen.Tool;

internal static class AtomicStateSourceRenderer
{
    internal static void AppendUpdatedResult(Action<string> line, EntityRepositoryModel entity, ConfiguredReturningStateModel state, string root)
    {
        // Keep the model filter on the fresh conflict-key read. Selection/version predicates may no longer match.
        line("        var __repoGenUpdated = await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(");
        line("            global::System.Linq.Queryable.Select(global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(" + root + "), entity => new");
        line("            {");
        line("                Entity = entity,");
        for (var index = 0; index < state.Properties.Count; index++)
        {
            var property = state.Properties[index];
            line("                Value" + index + " = global::Microsoft.EntityFrameworkCore.EF.Property<" + property.TypeName + ">(entity, " + JsonSerializer.Serialize(property.Name) + "),");
        }
        line("            }), __repoGenToken).ConfigureAwait(false);");
        line("        return new global::RonSijm.RepoGen.EntityFrameworkCore.ReturningWriteRow<" + entity.EntityTypeName + ">(__repoGenUpdated.Entity,");
        line("            new global::System.Collections.Generic.Dictionary<string, object?>");
        line("            {");
        for (var index = 0; index < state.Properties.Count; index++)
            line("                [" + JsonSerializer.Serialize(state.Properties[index].Name) + "] = __repoGenUpdated.Value" + index + ",");
        line("            });");
    }
}
