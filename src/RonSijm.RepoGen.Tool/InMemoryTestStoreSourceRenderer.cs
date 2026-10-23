using System.Text;
using System.Text.Json;

namespace RonSijm.RepoGen.Tool;

/// <summary>Emits arrangement infrastructure, deliberately independent of operation execution.</summary>
internal static class InMemoryTestStoreSourceRenderer
{
    private static readonly HashSet<string> ObjectMembers = new(StringComparer.Ordinal)
        { "Equals", "GetHashCode", "GetType", "ToString", "MemberwiseClone", "ReferenceEquals" };
    internal static string FixtureName(RepositoryGenerationModel model) => model.ContextTypeName.Split('.').Last() + "RepositoryFixture";
    internal static string DataName(RepositoryGenerationModel model) => model.ContextTypeName.Split('.').Last() + "RepositoryData";
    internal static string FixtureType(RepositoryGenerationModel model) => "global::" + model.Settings.ScriptedTestDoubleNamespace + "." + FixtureName(model);

    internal static void AppendFixture(StringBuilder builder, RepositoryGenerationModel model)
    {
        var entities = ScriptedTestDoubleSourceRenderer.Entities(model).Where(static entity => entity.InMemoryStore is not null).ToArray();
        foreach (var entity in entities.Where(static entity => entity.InMemoryStore!.ValueEqualities is { Count: > 0 }))
            InMemoryValueEqualitySourceRenderer.Append(builder, entity.InMemoryStore!.ValueEqualities!);
        foreach (var entity in entities.Where(static entity => entity.InMemoryStore!.EntityMapping is not null))
        {
            var mapping = entity.InMemoryStore!.EntityMapping!;
            builder.AppendLine().Append("    internal sealed class ").Append(mapping.ReadTypeName.Split('.').Last()).AppendLine("\n    {");
            foreach (var member in mapping.Members)
                builder.Append("        public ").Append(ObjectMembers.Contains(member.Name) ? "new " : "").Append("required ")
                    .Append(member.TypeName).Append(' ').Append(RepositoryModelFactory.EscapeIdentifier(member.Name))
                    .AppendLine(entity.InMemoryStore.WriteMapping is null ? " { get; init; }" : " { get; set; }");
            foreach (var member in mapping.Navigations ?? [])
                builder.Append("        public ").Append(ObjectMembers.Contains(member.Name) ? "new " : "")
                    .Append(member.IsCollection ? "global::System.Collections.Generic.List<" + member.TargetTypeName + ">" : member.TargetTypeName + (member.Required ? "" : "?"))
                    .Append(' ').Append(RepositoryModelFactory.EscapeIdentifier(member.Name))
                    .AppendLine(member.IsCollection ? " { get; set; } = new();" : member.Required ? " { get; set; } = null!;" : " { get; set; }");
            builder.AppendLine("    }");
        }
        var data = DataName(model);
        builder.AppendLine().Append("    public sealed class ").Append(data).Append(" : global::RonSijm.RepoGen.Testing.")
            .AppendLine(model.Settings.InMemoryTestCommands ? "InMemoryWriteDataContext\n    {" : "InMemoryDataContext\n    {");
        foreach (var entity in entities)
            builder.Append("        public global::RonSijm.RepoGen.Testing.InMemoryStore<").Append(entity.InMemoryStore!.RowTypeName).Append(", ").Append(entity.InMemoryStore.KeyTypeName)
                .Append("> ").Append(RepositoryModelFactory.EscapeIdentifier(entity.EntityName + "Store")).AppendLine(" { get; }");
        foreach (var entity in entities.Where(static entity => entity.InMemoryStore!.WriteMapping is not null))
            builder.Append("        internal global::RonSijm.RepoGen.Testing.InMemoryWriteStore<").Append(entity.InMemoryStore!.EntityMapping!.ReadTypeName).Append(", ").Append(entity.InMemoryStore.KeyTypeName)
                .Append("> ").Append(RepositoryModelFactory.EscapeIdentifier("__RepoGen" + entity.EntityName + "Writes")).AppendLine(" { get; }");
        builder.Append("        public ").Append(data).AppendLine("()\n        {");
        foreach (var entity in entities)
        {
            builder.Append("            ").Append(RepositoryModelFactory.EscapeIdentifier(entity.EntityName + "Store")).Append(" = CreateStore<").Append(entity.InMemoryStore!.RowTypeName)
                .Append(", ").Append(entity.InMemoryStore!.KeyTypeName).Append(">(").Append(JsonSerializer.Serialize(entity.EntityTypeName))
                .Append(", ").Append(entity.InMemoryStore.KeySelectorSource).Append(", ").Append(entity.InMemoryStore.CopySource);
            if (entity.InMemoryStore.KeyComparerSource is { } comparer)
                builder.Append(", keyComparer: ").Append(comparer);
            builder.AppendLine(");");
        }
        foreach (var entity in entities.Where(static entity => entity.InMemoryStore!.WriteMapping is not null))
        {
            var store = entity.InMemoryStore!; var write = store.WriteMapping!;
            builder.Append("            ").Append(RepositoryModelFactory.EscapeIdentifier("__RepoGen" + entity.EntityName + "Writes")).Append(" = MapWriteStore<")
                .Append(store.RowTypeName).Append(", ").Append(store.EntityMapping!.ReadTypeName).Append(", ").Append(store.KeyTypeName).Append(">(")
                .Append(RepositoryModelFactory.EscapeIdentifier(entity.EntityName + "Store")).Append(", ").Append(store.EntityMapping.ExpressionSource).Append(", ")
                .Append(write.WriteSource).Append(", ").Append(write.CopySource).Append(", ").Append(write.KeySource).Append(", ").Append(write.SameValuesSource).AppendLine(");");
        }
        foreach (var entity in entities) InMemoryGeneratedValueSourceRenderer.Append(builder, entity);
        builder.AppendLine("        }\n    }");

        builder.AppendLine().Append("    public sealed class ").Append(FixtureName(model)).AppendLine("\n    {");
        if (model.Settings.ContextKeyTypeName is { } key)
        {
            var type = "global::RonSijm.RepoGen.Testing.KeyedInMemoryFixture<" + key + ", " + data + ">";
            builder.Append("        private readonly ").Append(type).AppendLine(" partitions = new();");
            builder.Append("        public global::System.Collections.Generic.IReadOnlyList<").Append(key).AppendLine("> ContextKeys => partitions.Keys;");
            builder.Append("        public ").Append(data).Append(" For(").Append(key).AppendLine(" contextKey) => partitions.For(contextKey);");
        }
        else builder.Append("        public ").Append(data).AppendLine(" Data { get; } = new();");
        builder.AppendLine("    }");
    }
}
