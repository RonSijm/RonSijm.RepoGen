using System.Text;

namespace RonSijm.RepoGen.Tool;

internal static class InMemoryGeneratedValueSourceRenderer
{
    internal static void Append(StringBuilder builder, EntityRepositoryModel entity)
    {
        if (entity.InMemoryStore!.GeneratedValues is not { Count: > 0 } values) return;
        builder.Append("            ConfigureGeneratedValues(").Append(InMemoryCommandSourceRenderer.StoreName(entity)).AppendLine(",");
        Callback("onCreate", values.Where(value => value.OnCreate).ToArray(), create: true);
        builder.AppendLine(",");
        Callback("onUpdate", values.Where(value => value.OnUpdate).ToArray(), create: false);
        builder.AppendLine(");");

        void Callback(string name, IReadOnlyList<InMemoryGeneratedValueModel> selected, bool create)
        {
            builder.Append("                ").Append(name).Append(": ");
            if (selected.Count == 0) { builder.Append("null"); return; }
            builder.AppendLine("static (__repoGenEntity, __repoGenRows) =>\n                {");
            foreach (var value in selected)
            {
                if (create && value.CreateCondition != "true") builder.Append("                    if (").Append(value.CreateCondition).AppendLine(")");
                builder.Append("                        __repoGenEntity.").Append(RepositoryModelFactory.EscapeIdentifier(value.PropertyName)).Append(" = ").Append(value.ValueSource).AppendLine(";");
            }
            builder.Append("                }");
        }
    }
}
