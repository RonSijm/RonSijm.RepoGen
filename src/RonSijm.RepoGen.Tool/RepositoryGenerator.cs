using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

public enum RepositoryOutputKind
{
    Repositories,
    Contracts,
    Projections,
    Manifest,
    ApiSnapshot,
    TestDoubles
}

public enum RepositoryOutputLayout
{
    SingleFile,
    PerEntity,
    PerContract
}

public sealed record GeneratedRepositoryFile(RepositoryOutputKind Kind, string Name, string Contents);

public sealed record RepositoryOutputLocations(string Repositories, string? Contracts = null, string? Projections = null, string? Manifest = null, string? ApiSnapshot = null, string? TestDoubles = null);

public sealed record RepositoryOutputDifference(string Path, string DiagnosticId, string Message);

/// <summary>The same design-time generation engine used by repogen, callable from an independently buildable C# runner.</summary>
public static class RepositoryGenerator
{
    /// <summary>Generates independently configured contexts for one assembly, validating type/port/path collisions before any writes.</summary>
    public static RepositoryGenerationBatch GenerateMany(params RepositoryGenerationRequest[] requests) => new(requests);

    public static RepositoryGenerationBatch GenerateMany(IEnumerable<RepositoryGenerationRequest> requests) => new(requests);

    public static RepositoryGenerationBundle Generate(DbContext context, RepositoryDesigner designer,
        RepositoryOutputLayout layout = RepositoryOutputLayout.SingleFile, bool generateSelectors = true)
    {
        ArgumentNullException.ThrowIfNull(designer);
        return Generate(context, designer.CreateModel(), layout, generateSelectors);
    }

    public static RepositoryGenerationBundle Generate(DbContext context, RepositoryDesign? design = null,
        RepositoryOutputLayout layout = RepositoryOutputLayout.SingleFile, bool generateSelectors = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Generate(RepositoryModelFactory.Create(context, context.GetType().Assembly, generateSelectors, design), layout);
    }

    internal static RepositoryGenerationBundle Generate(RepositoryGenerationModel model, RepositoryOutputLayout layout = RepositoryOutputLayout.SingleFile)
    {
        if (!Enum.IsDefined(layout))
        {
            throw new ArgumentOutOfRangeException(nameof(layout));
        }

        RepositoryGeneratedTypes.Validate([model]);
        var files = new List<GeneratedRepositoryFile>();
        // Validate whole-model names and port collisions before splitting output.
        var contracts = model.Settings.ContractNamespace is null ? null : ClosedRepositorySourceRenderer.RenderContracts(model);
        if (layout == RepositoryOutputLayout.PerContract && contracts is null)
        {
            throw new InvalidOperationException("RGG0006: PerContract output requires closed repositories.");
        }

        if (layout == RepositoryOutputLayout.SingleFile)
        {
            files.Add(new(RepositoryOutputKind.Repositories, "Repositories.g.cs", RepositorySourceRenderer.Render(model)));
            if (contracts is not null)
            {
                files.Add(new(RepositoryOutputKind.Contracts, "Contracts.g.cs", contracts));
            }
            else if (model.Entities.Any(static entity => entity.Projections.Count > 0 || entity.Queries.Count > 0))
            {
                files.Add(new(RepositoryOutputKind.Projections, "Projections.g.cs", ProjectionSourceRenderer.Render(model)));
            }
        }
        else
        {
            files.Add(new(RepositoryOutputKind.Repositories, model.Settings.GeneratedNamespace is null
                ? "RepositoryInfrastructure.g.cs"
                : model.Settings.GeneratedNamespace + "." + model.ContextTypeName.Split('<')[0].Split('.').Last() + ".RepositoryInfrastructure.g.cs", RepositorySourceRenderer.RenderInfrastructure(model)));
            foreach (var entity in model.Entities)
            {
                var partial = model with { Entities = [entity] };
                var prefix = string.IsNullOrEmpty(entity.Namespace) ? entity.EntityName : entity.Namespace + "." + entity.EntityName;
                files.Add(new(RepositoryOutputKind.Repositories, prefix + ".Repository.g.cs", RepositorySourceRenderer.Render(partial, infrastructure: false, closedRegistration: false)));
                if (layout == RepositoryOutputLayout.PerContract)
                {
                    foreach (var name in ClosedRepositorySourceRenderer.ContractNames(entity).Concat(entity.ReadEnabled || entity.WriteEnabled ? ["I" + entity.EntityName + "Repository"] : Array.Empty<string>()))
                    {
                        files.Add(new(RepositoryOutputKind.Contracts, model.Settings.ContractNamespace + "." + name + ".g.cs", ClosedRepositorySourceRenderer.RenderContracts(partial, name)));
                    }
                }
                else if (contracts is not null)
                {
                    files.Add(new(RepositoryOutputKind.Contracts, prefix + ".Contracts.g.cs", ClosedRepositorySourceRenderer.RenderContracts(partial)));
                }
                else if (entity.Projections.Count > 0 || entity.Queries.Count > 0)
                {
                    files.Add(new(RepositoryOutputKind.Projections, prefix + ".Projections.g.cs", ProjectionSourceRenderer.Render(partial)));
                }
            }
        }

        if (ScriptedTestDoubleSourceRenderer.Entities(model).Any())
        {
            // One testing file in SingleFile mode; split entity files and a context registration otherwise.
            if (layout == RepositoryOutputLayout.SingleFile)
                files.Add(new(RepositoryOutputKind.TestDoubles, "RepositoryTestDoubles.g.cs", ScriptedTestDoubleSourceRenderer.Render(model)));
            else
            {
                files.Add(new(RepositoryOutputKind.TestDoubles, model.Settings.ScriptedTestDoubleNamespace + "." + model.ContextTypeName.Split('.').Last() + ".TestDoubleRegistration.g.cs",
                    ScriptedTestDoubleSourceRenderer.Render(model, declarations: false)));
                foreach (var entity in ScriptedTestDoubleSourceRenderer.Entities(model))
                    files.Add(new(RepositoryOutputKind.TestDoubles, model.Settings.ScriptedTestDoubleNamespace + "." + entity.EntityName + ".TestDouble.g.cs",
                        ScriptedTestDoubleSourceRenderer.Render(model with { Entities = [entity] }, registration: false)));
            }
        }

        files.Add(new(RepositoryOutputKind.Manifest, "manifest.json", RepositoryManifestRenderer.Render(model)));
        var marked = files.Select(file => file.Kind == RepositoryOutputKind.Manifest
            ? file
            : file with
            {
                Contents = file.Contents.Replace("// <auto-generated/>", "// <auto-generated/>" + Environment.NewLine + "// RepoGen context: " + model.ContextTypeName, StringComparison.Ordinal)
            }).ToArray();
        return new(Array.AsReadOnly(marked), model.Entities.Count, layout, model.ContextTypeName, model);
    }
}

public sealed class RepositoryGenerationBundle
{
    private readonly RepositoryGenerationModel model;
    internal RepositoryGenerationModel Model => model;

    internal RepositoryGenerationBundle(IReadOnlyList<GeneratedRepositoryFile> files, int entityCount, RepositoryOutputLayout layout, string context, RepositoryGenerationModel model)
    {
        Files = files;
        EntityCount = entityCount;
        Layout = layout;
        Context = context;
        this.model = model;
    }

    public string Context { get; }
    public IReadOnlyList<GeneratedRepositoryFile> Files { get; }
    public int EntityCount { get; }
    public RepositoryOutputLayout Layout { get; }

    public RepositoryApiSnapshot CreateApiSnapshot() => RepositoryApiSnapshot.Capture(model);

    /// <summary>Explicitly include an API snapshot output. Its destination is a filename in every layout; not an automatically accepted baseline.</summary>
    public RepositoryGenerationBundle WithApiSnapshot() => new(Array.AsReadOnly(Files.Where(static file => file.Kind != RepositoryOutputKind.ApiSnapshot)
        .Append(new GeneratedRepositoryFile(RepositoryOutputKind.ApiSnapshot, "RepositoryApi.json", CreateApiSnapshot().ToJson())).ToArray()), EntityCount, Layout, Context, model);

    internal IEnumerable<string> GetOutputPaths(RepositoryOutputLocations locations) => Resolve(locations).Select(static output => output.Path);

    public IReadOnlyList<RepositoryOutputDifference> Check(RepositoryOutputLocations locations)
    {
        var outputs = Resolve(locations);
        var differences = outputs.Where(output => !File.Exists(output.Path) || !string.Equals(File.ReadAllText(output.Path), output.File.Contents, StringComparison.Ordinal))
            .Select(static output => new RepositoryOutputDifference(output.Path, "RGG0001", "Generated output is missing or stale.")).ToList();
        if (Layout != RepositoryOutputLayout.SingleFile)
        {
            var expected = outputs.Select(static output => output.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in outputs.Where(static output => output.File.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.ApiSnapshot).Select(static output => Path.GetDirectoryName(output.Path)!).Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
            {
                foreach (var path in Directory.EnumerateFiles(directory, "*.g.cs").Order(StringComparer.Ordinal))
                {
                    if (!expected.Contains(path) && File.ReadLines(path).Any(line => line == "// RepoGen context: " + Context))
                    {
                        differences.Add(new(path, "RGG0004", "Obsolete generated file for this context; review and remove it explicitly."));
                    }
                }
            }
        }

        return differences.AsReadOnly();
    }

    /// <summary>Preflights all destinations before replacing anything; only generator-owned files may be replaced.</summary>
    /// <remarks>Writes use per-file atomic replacement, not a multi-file filesystem transaction. Obsolete split files are reported, never deleted automatically.</remarks>
    public IReadOnlyList<string> Write(RepositoryOutputLocations locations)
    {
        var outputs = Resolve(locations);
        Preflight(outputs, Context);
        return WritePrepared(outputs);
    }

    internal static void Preflight((string Path, GeneratedRepositoryFile File)[] outputs, string context)
    {
        foreach (var output in outputs)
        {
            if (Directory.Exists(output.Path))
            {
                throw new InvalidOperationException($"RGG0002: Output destination is a directory: {output.Path}");
            }

            for (var parent = Path.GetDirectoryName(output.Path); parent is not null; parent = Path.GetDirectoryName(parent))
            {
                if (File.Exists(parent))
                {
                    throw new InvalidOperationException($"RGG0002: Output parent is an existing file: {parent}");
                }
            }

            if (!File.Exists(output.Path))
            {
                continue;
            }

            if (new FileInfo(output.Path).LinkTarget is not null)
            {
                throw new InvalidOperationException($"RGG0002: Refusing to replace a linked output: {output.Path}");
            }

            var existing = File.ReadAllText(output.Path);
            if (string.Equals(existing, output.File.Contents, StringComparison.Ordinal))
            {
                continue;
            }

            if (!Owned(existing, output.File.Kind))
            {
                throw new InvalidOperationException($"RGG0003: Refusing to overwrite a handwritten file: {output.Path}");
            }

            if (ExistingContext(existing, output.File.Kind) is { } owner && owner != context)
            {
                throw new InvalidOperationException($"RGG0008: Output '{output.Path}' belongs to '{owner}', not '{context}'. Use a distinct destination; context ownership is not transferable by regeneration.");
            }
        }
    }

    internal static IReadOnlyList<string> WritePrepared((string Path, GeneratedRepositoryFile File)[] outputs)
    {
        var changed = new List<string>();
        foreach (var output in outputs)
        {
            if (File.Exists(output.Path) && string.Equals(File.ReadAllText(output.Path), output.File.Contents, StringComparison.Ordinal))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output.Path)!);
            var temporary = output.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, output.File.Contents);
                File.Move(temporary, output.Path, overwrite: true);
                changed.Add(output.Path);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        return changed.AsReadOnly();
    }

    internal (string Path, GeneratedRepositoryFile File)[] Resolve(RepositoryOutputLocations locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        var outputs = new List<(string Path, GeneratedRepositoryFile File)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files)
        {
            var destination = file.Kind switch
            {
                RepositoryOutputKind.Repositories => locations.Repositories,
                RepositoryOutputKind.Contracts => locations.Contracts,
                RepositoryOutputKind.Projections => locations.Projections,
                RepositoryOutputKind.Manifest => locations.Manifest,
                RepositoryOutputKind.ApiSnapshot => locations.ApiSnapshot,
                RepositoryOutputKind.TestDoubles => locations.TestDoubles,
                _ => null
            };
            if (destination is null)
            {
                if (file.Kind == RepositoryOutputKind.Manifest)
                {
                    continue;
                }

                throw new InvalidOperationException($"RGG0002: A {file.Kind} output location is required.");
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(destination);
            var path = Path.GetFullPath(Layout != RepositoryOutputLayout.SingleFile && file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.ApiSnapshot ? Path.Combine(destination, file.Name) : destination);
            if (!names.Add(path))
            {
                throw new InvalidOperationException($"RGG0002: Output files must use distinct paths: {path}");
            }

            outputs.Add((path, file));
        }

        foreach (var output in outputs)
        {
            for (var parent = Path.GetDirectoryName(output.Path); parent is not null; parent = Path.GetDirectoryName(parent))
            {
                if (names.Contains(parent))
                {
                    throw new InvalidOperationException($"RGG0002: Output '{parent}' is also a parent directory of '{output.Path}'.");
                }
            }
        }

        return outputs.ToArray();
    }

    private static string? ExistingContext(string contents, RepositoryOutputKind kind)
    {
        if (kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.ApiSnapshot)
        {
            return contents.Split('\n').Select(static line => line.TrimEnd('\r'))
                .FirstOrDefault(static line => line.StartsWith("// RepoGen context: ", StringComparison.Ordinal))?["// RepoGen context: ".Length..];
        }

        using var document = JsonDocument.Parse(contents);
        return document.RootElement.TryGetProperty("context", out var context) && context.ValueKind == JsonValueKind.String ? context.GetString() : null;
    }

    private static bool Owned(string contents, RepositoryOutputKind kind)
    {
        if (kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.ApiSnapshot)
        {
            return contents.TrimStart().StartsWith("// <auto-generated/>", StringComparison.Ordinal);
        }

        try
        {
            if (kind == RepositoryOutputKind.ApiSnapshot)
            {
                RepositoryApiSnapshot.Parse(contents);
                return true;
            }

            using var document = JsonDocument.Parse(contents);
            var root = document.RootElement;
            return root.TryGetProperty("generatedBy", out var generator) && generator.GetString() == "RonSijm.RepoGen" ||
                   // Earlier RepoGen manifests had no generatedBy marker. Recognize that exact inventory format during migration.
                   root.TryGetProperty("schemaVersion", out var schema) && schema.GetInt32() == 1 && root.TryGetProperty("surface", out _) &&
                   root.TryGetProperty("context", out _) && root.TryGetProperty("entities", out _) && root.TryGetProperty("features", out _);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentException) { return false; }
    }
}
