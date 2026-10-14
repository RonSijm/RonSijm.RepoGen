using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RonSijm.RepoGen.Tool;

/// <summary>Separate test-only output. Reuses the closed API inventory, including exact bound overload annotations.</summary>
internal static class ScriptedTestDoubleSourceRenderer
{
    internal static IEnumerable<EntityRepositoryModel> Entities(RepositoryGenerationModel model)
        => model.Entities.Where(static entity => entity.GenerateTestDouble && (entity.ReadEnabled || entity.WriteEnabled));

    internal static IReadOnlyList<ScriptedTestDoubleOperationModel> Operations(RepositoryGenerationModel model, EntityRepositoryModel entity)
    {
        var signatures = entity.Queries.Select(RepositoryOperationSignature.Query).Concat(entity.Commands.Select(RepositoryOperationSignature.Command)).ToHashSet(StringComparer.Ordinal);
        var inventory = RepositoryApiSnapshot.Capture(model with { Entities = [entity] }).Contracts.SelectMany(static contract => contract.Methods).ToArray();
        if (inventory.Any(static method => method.Signature.Split('(')[0] is "ResetTestDouble" or "ClearCalls"))
            throw new InvalidOperationException("RGD3102: Application ports cannot declare ResetTestDouble/ClearCalls when generating scripted test doubles. These names are reserved for fixture controls.");
        if (model.Settings.InMemoryTestStores && inventory.Any(static method => method.Signature.Split('(')[0] == "Fixture"))
            throw new InvalidOperationException("RGD3202: Application ports cannot declare Fixture when generating in-memory test stores. This name is reserved for the shared fixture property.");
        if ((model.Settings.InMemoryTestQueries || model.Settings.InMemoryTestCommands) && inventory.Any(static method => method.Signature.Split('(')[0] == "__repoGenThenBy" || method.Signature.StartsWith("__repoGenExecuteOn", StringComparison.Ordinal)))
            throw new InvalidOperationException("RGD3303: Test-double operation names collide with reserved in-memory execution helpers. Rename the designer operation.");
        var methods = inventory.Where(method => signatures.Contains(method.Signature)).DistinctBy(static method => method.Signature)
            .OrderBy(static method => method.Signature, StringComparer.Ordinal).ToArray();
        var candidates = methods.Select(method =>
        {
            var name = method.Signature.Split('(')[0];
            var overloaded = methods.Count(candidate => candidate.Signature.Split('(')[0] == name) > 1;
            var script = "On" + name + (overloaded ? "By" + string.Join("And", method.Parameters.SkipLast(1).Select(static parameter => TypeLabel(parameter.Type))) : "");
            return (Method: method, Script: script);
        }).ToArray();
        var reserved = new HashSet<string>(StringComparer.Ordinal) { entity.EntityName + "RepositoryTestDouble", "ResetTestDouble", "ClearCalls" };
        if (model.Settings.InMemoryTestStores) reserved.Add("Fixture");
        foreach (var method in inventory) reserved.Add(method.Signature.Split('(')[0]);
        var operations = new List<ScriptedTestDoubleOperationModel>();
        foreach (var candidate in candidates)
        {
            var method = candidate.Method;
            var script = candidate.Script;
            if (script.Length > 120 || candidates.Count(other => other.Script == script) > 1 || reserved.Contains(script) || reserved.Contains(script + "Arguments"))
                script = "On" + method.Signature.Split('(')[0] + "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(method.Signature)))[..16];
            if (!reserved.Add(script) || !reserved.Add(script + "Arguments"))
                throw new InvalidOperationException("RGD3102: Test-double member names collide for '" + method.Signature + "'. Rename the designer operation.");

            const string task = "global::System.Threading.Tasks.Task<";
            const string stream = "global::System.Collections.Generic.IAsyncEnumerable<";
            var isStream = method.ReturnType.StartsWith(stream, StringComparison.Ordinal);
            var prefix = isStream ? stream : task;
            if (!method.ReturnType.StartsWith(prefix, StringComparison.Ordinal) || !method.ReturnType.EndsWith('>') ||
                method.Parameters.Count == 0 || method.Parameters[^1].Type != "global::System.Threading.CancellationToken")
                throw new InvalidOperationException("RGD3102: Unsupported scripted contract shape: " + method.Signature + ". Expected Task<T>/IAsyncEnumerable<T> and a final CancellationToken.");
            operations.Add(new(method, script, method.ReturnType[prefix.Length..^1], isStream));
        }

        return operations.AsReadOnly();
    }

    internal static string Render(RepositoryGenerationModel model, bool registration = true, bool declarations = true)
    {
        var ns = model.Settings.ScriptedTestDoubleNamespace ?? throw new InvalidOperationException("RGD3101: A testing namespace is required.");
        var builder = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        builder.Append("namespace ").Append(ns).AppendLine("\n{");
        if (registration && model.Settings.InMemoryTestStores) InMemoryTestStoreSourceRenderer.AppendFixture(builder, model);
        foreach (var entity in declarations ? Entities(model) : [])
        {
            var name = entity.EntityName + "RepositoryTestDouble";
            var operations = Operations(model, entity);
            builder.AppendLine().Append("    public interface I").Append(name).Append(" : global::").Append(model.Settings.ContractNamespace).Append(".I").Append(entity.EntityName).AppendLine("Repository");
            builder.AppendLine("    {");
            foreach (var operation in operations)
                builder.Append("        ").Append(ScriptType(ns, name, operation)).Append(' ').Append(operation.ScriptName).AppendLine(" { get; }");
            if (model.Settings.InMemoryTestStores)
                builder.Append("        ").Append(InMemoryTestStoreSourceRenderer.FixtureType(model)).AppendLine(" Fixture { get; }");
            builder.AppendLine("        void ResetTestDouble();\n        void ClearCalls();\n    }");
            builder.AppendLine().Append("    public sealed class ").Append(name).Append(" : I").Append(name).AppendLine("\n    {");
            if (model.Settings.InMemoryTestStores)
            {
                builder.Append("        public ").Append(InMemoryTestStoreSourceRenderer.FixtureType(model)).AppendLine(" Fixture { get; }");
                builder.Append("        public ").Append(name).Append('(').Append(InMemoryTestStoreSourceRenderer.FixtureType(model)).Append(" fixture)\n        {\n            global::System.ArgumentNullException.ThrowIfNull(fixture);\n            Fixture = fixture;\n");
                if (model.Settings.InMemoryTestQueries || model.Settings.InMemoryTestCommands)
                    foreach (var operation in operations)
                        builder.Append("            ").Append(operation.ScriptName).Append(" = new(")
                            .Append(JsonSerializer.Serialize(model.ContextTypeName + ":" + entity.EntityName + "." + operation.Method.Signature)).Append(", ")
                            .Append(InMemoryQuerySourceRenderer.HandlerName(operation)).AppendLine(");");
                builder.AppendLine("        }");
            }
            foreach (var operation in operations)
            {
                AppendArguments(builder, operation);
                builder.Append("        public ").Append(ScriptType(ns, name, operation)).Append(' ').Append(operation.ScriptName)
                    .Append(" { get; }");
                if (!model.Settings.InMemoryTestQueries && !model.Settings.InMemoryTestCommands)
                    builder.Append(" = new(").Append(JsonSerializer.Serialize(model.ContextTypeName + ":" + entity.EntityName + "." + operation.Method.Signature)).Append(");");
                builder.AppendLine();
                builder.Append("        public ").Append(operation.Method.ReturnType).Append(' ').Append(operation.Name).Append('(');
                builder.Append(string.Join(", ", operation.Method.Parameters.Select((parameter, index) => parameter.Type + " " + RepositoryModelFactory.EscapeIdentifier(parameter.Name) +
                    (index == operation.Method.Parameters.Count - 1 ? " = default" : ""))));
                builder.Append(") => ").Append(operation.ScriptName).Append(operation.Stream ? ".InvokeStream(new " : ".InvokeAsync(new ")
                    .Append(operation.ArgumentsName).Append('(').Append(string.Join(", ", operation.Inputs.Select(static parameter => RepositoryModelFactory.EscapeIdentifier(parameter.Name))))
                    .Append("), ").Append(RepositoryModelFactory.EscapeIdentifier(operation.Method.Parameters[^1].Name)).AppendLine(");");
                if (InMemoryCommandSourceRenderer.Command(entity, operation)?.InMemoryCommand is { Supported: true })
                    InMemoryCommandSourceRenderer.AppendHandler(builder, model, entity, operation);
                else if (model.Settings.InMemoryTestQueries || model.Settings.InMemoryTestCommands)
                    InMemoryQuerySourceRenderer.AppendHandler(builder, model, entity, operation);
            }

            if (model.Settings.InMemoryTestQueries && entity.Queries.Any(static query => query.InMemoryQuery is { Supported: true }))
                InMemoryQuerySourceRenderer.AppendOrderingHelper(builder);

            foreach (var reset in new[] { (Name: "ResetTestDouble", Call: "Reset"), (Name: "ClearCalls", Call: "ClearCalls") })
            {
                builder.Append("        public void ").Append(reset.Name).AppendLine("()\n        {");
                foreach (var operation in operations) builder.Append("            ").Append(operation.ScriptName).Append('.').Append(reset.Call).AppendLine("();");
                builder.AppendLine("        }");
            }

            builder.AppendLine("    }");
        }

        if (registration) AppendRegistration(builder, model);
        builder.AppendLine("}");
        return builder.ToString();
    }

    internal static IEnumerable<string> Names(RepositoryGenerationModel model)
    {
        if (!Entities(model).Any()) yield break;
        var ns = model.Settings.ScriptedTestDoubleNamespace!;
        yield return ns + "." + ContextName(model) + "RepositoryTestDoubleRegistration";
        if (model.Settings.InMemoryTestStores)
        {
            yield return ns + "." + InMemoryTestStoreSourceRenderer.FixtureName(model);
            yield return ns + "." + InMemoryTestStoreSourceRenderer.DataName(model);
            foreach (var mapping in Entities(model).Select(static entity => entity.InMemoryStore?.EntityMapping).OfType<InMemoryEntityMappingModel>())
                yield return mapping.ReadTypeName["global::".Length..];
            foreach (var policies in Entities(model).Select(static entity => entity.InMemoryStore?.ValueEqualities).OfType<IReadOnlyList<InMemoryValueEqualityModel>>())
                if (policies.Count > 0) yield return policies[0].HelperTypeName["global::".Length..];
        }
        foreach (var entity in Entities(model))
        {
            yield return ns + "." + entity.EntityName + "RepositoryTestDouble";
            yield return ns + ".I" + entity.EntityName + "RepositoryTestDouble";
        }
    }

    private static string ScriptType(string ns, string name, ScriptedTestDoubleOperationModel operation)
        => "global::RonSijm.RepoGen.Testing." + (operation.Stream ? "ScriptedStreamOperation<" : "ScriptedOperation<") +
           "global::" + ns + "." + name + "." + operation.ArgumentsName + ", " + operation.ResultType + ">";

    internal static string[] ArgumentPropertyNames(ScriptedTestDoubleOperationModel operation)
    {
        var inputs = operation.Inputs.ToArray();
        var properties = new HashSet<string>(StringComparer.Ordinal) { operation.ArgumentsName };
        return inputs.Select((parameter, index) =>
        {
            var name = char.ToUpperInvariant(parameter.Name[0]) + parameter.Name[1..];
            while (!properties.Add(name)) name += "_" + index;
            return RepositoryModelFactory.EscapeIdentifier(name);
        }).ToArray();
    }

    private static void AppendArguments(StringBuilder builder, ScriptedTestDoubleOperationModel operation)
    {
        var inputs = operation.Inputs.ToArray();
        var names = ArgumentPropertyNames(operation);
        builder.AppendLine().Append("        public sealed class ").Append(operation.ArgumentsName).AppendLine("\n        {");
        for (var index = 0; index < inputs.Length; index++)
        {
            var hidesObject = typeof(object).GetMembers().Any(member => member.Name == names[index].TrimStart('@'));
            builder.Append("            public ").Append(hidesObject ? "new " : "").Append(inputs[index].Type).Append(' ').Append(names[index]).AppendLine(" { get; }");
        }

        builder.Append("            public ").Append(operation.ArgumentsName).Append('(')
            .Append(string.Join(", ", inputs.Select(static parameter => parameter.Type + " " + RepositoryModelFactory.EscapeIdentifier(parameter.Name)))).AppendLine(")\n            {");
        for (var index = 0; index < inputs.Length; index++)
            builder.Append("                this.").Append(names[index]).Append(" = ").Append(RepositoryModelFactory.EscapeIdentifier(inputs[index].Name)).AppendLine(";");
        builder.AppendLine("            }\n        }");
    }

    private static string TypeLabel(string type)
    {
        var nullable = type.EndsWith('?');
        var words = Regex.Matches(type.Replace("global::", "", StringComparison.Ordinal), "[A-Za-z_][A-Za-z0-9_]*");
        var name = words.Count == 0 ? "Value" : words[^1].Value;
        return (nullable ? "Nullable" : "") + char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static string ContextName(RepositoryGenerationModel model) => model.ContextTypeName.Split('.').Last();

    private static void AppendRegistration(StringBuilder builder, RepositoryGenerationModel model)
    {
        var ns = model.Settings.ScriptedTestDoubleNamespace!;
        var context = ContextName(model);
        builder.AppendLine().Append("    public static class ").Append(context).AppendLine("RepositoryTestDoubleRegistration\n    {");
        builder.Append("        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add").Append(context)
            .AppendLine("RepositoryTestDoubles(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)\n        {\n            global::System.ArgumentNullException.ThrowIfNull(services);");
        if (model.Settings.InMemoryTestStores)
            builder.Append("            global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<")
                .Append(InMemoryTestStoreSourceRenderer.FixtureType(model)).AppendLine(">(services);");
        foreach (var entity in Entities(model))
        {
            var concrete = "global::" + ns + "." + entity.EntityName + "RepositoryTestDouble";
            var aliases = ClosedRepositorySourceRenderer.ContractNames(entity).Append("I" + entity.EntityName + "Repository")
                .Select(name => "global::" + model.Settings.ContractNamespace + "." + name)
                .Concat((entity.ReadPorts ?? []).Concat(entity.WritePorts ?? []).Select(static port => port.TypeName))
                .Append("global::" + ns + ".I" + entity.EntityName + "RepositoryTestDouble").Distinct(StringComparer.Ordinal).ToArray();
            // Explicit test registration replaces production aliases; repeated calls do not duplicate concrete fixtures.
            builder.Append("            global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<").Append(concrete).AppendLine(">(services);");
            foreach (var alias in aliases)
            {
                builder.Append("            global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<").Append(alias).AppendLine(">(services);");
                builder.Append("            global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<").Append(alias)
                    .Append(">(services, static provider => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<")
                    .Append(concrete).AppendLine(">(provider));");
            }
        }

        builder.AppendLine("            return services;\n        }\n    }");
    }
}
