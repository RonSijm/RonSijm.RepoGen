using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

/// <summary>One context/designer/output entry in an explicitly generated application batch. The caller owns the context.</summary>
public sealed record RepositoryGenerationRequest(
    DbContext Context,
    RepositoryOutputLocations Output,
    RepositoryDesign? Design = null,
    RepositoryOutputLayout Layout = RepositoryOutputLayout.SingleFile,
    bool GenerateSelectors = true,
    bool IncludeApiSnapshot = false);

/// <summary>A validated immutable set of outputs. Generating/checking never creates directories or writes files.</summary>
public sealed class RepositoryGenerationBatch
{
    private readonly (RepositoryGenerationBundle Bundle, RepositoryOutputLocations Output)[] entries;

    internal RepositoryGenerationBatch(IEnumerable<RepositoryGenerationRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var inputs = requests.ToArray();
        if (inputs.Length == 0)
        {
            throw new ArgumentException("At least one generation request is required.", nameof(requests));
        }

        foreach (var request in inputs)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Context);
            ArgumentNullException.ThrowIfNull(request.Output);
        }

        var duplicate = inputs.GroupBy(static request => request.Context.GetType()).FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"RGG0010: Context '{duplicate.Key.FullName}' occurs more than once in this generation batch.");
        }

        // Context ordering is independent of module/caller registration order; each designer keeps its own explicit module order.
        entries = inputs.OrderBy(static request => request.Context.GetType().FullName, StringComparer.Ordinal).Select(request =>
        {
            var bundle = RepositoryGenerator.Generate(request.Context, request.Design, request.Layout, request.GenerateSelectors);
            return (request.IncludeApiSnapshot ? bundle.WithApiSnapshot() : bundle, request.Output);
        }).ToArray();
        RepositoryGeneratedTypes.Validate(entries.Select(static entry => entry.Bundle.Model));
        ValidatePaths();
        Bundles = Array.AsReadOnly(entries.Select(static entry => entry.Bundle).ToArray());
    }

    public IReadOnlyList<RepositoryGenerationBundle> Bundles { get; }
    public int EntityCount => Bundles.Sum(static bundle => bundle.EntityCount);

    public IReadOnlyList<RepositoryOutputDifference> Check() => Array.AsReadOnly(entries
        .SelectMany(static entry => entry.Bundle.Check(entry.Output)).OrderBy(static difference => difference.Path, StringComparer.Ordinal).ToArray());

    /// <summary>Preflights every context's destination and ownership before replacing any output.</summary>
    /// <remarks>Per-file replacement is atomic; this is not a multi-file transaction. Obsolete outputs are reported, never deleted.</remarks>
    public IReadOnlyList<string> Write()
    {
        var outputs = ValidatePaths();
        foreach (var entry in entries)
        {
            RepositoryGenerationBundle.Preflight(entry.Bundle.Resolve(entry.Output), entry.Bundle.Context);
        }

        return RepositoryGenerationBundle.WritePrepared(outputs);
    }

    private (string Path, GeneratedRepositoryFile File)[] ValidatePaths()
    {
        var outputs = entries.SelectMany(static entry => entry.Bundle.Resolve(entry.Output)).OrderBy(static output => output.Path, StringComparer.Ordinal).ToArray();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var output in outputs)
        {
            if (!paths.Add(output.Path))
            {
                throw new InvalidOperationException($"RGG0002: Generation batch outputs must use distinct paths: {output.Path}");
            }
        }

        foreach (var output in outputs)
        {
            for (var parent = Path.GetDirectoryName(output.Path); parent is not null; parent = Path.GetDirectoryName(parent))
            {
                if (paths.Contains(parent))
                {
                    throw new InvalidOperationException($"RGG0002: Output '{parent}' is also a parent directory of '{output.Path}'.");
                }
            }
        }

        return outputs;
    }
}