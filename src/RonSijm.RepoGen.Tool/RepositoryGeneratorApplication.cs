using System.Globalization;

namespace RonSijm.RepoGen.Tool;

internal static class RepositoryGeneratorApplication
{
    public static int Run(string[] args)
    {
        if (!GeneratorOptions.TryParse(args, out var options, out var error))
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.Error.WriteLine(error);
                Console.Error.WriteLine();
            }

            WriteUsage(error is null ? Console.Out : Console.Error);
            return error is null ? 0 : 2;
        }

        try
        {
            var targetPath = ProjectCompiler.GetTargetAssembly(options!);
            var designerPath = options!.DesignerProjectPath is null
                ? null
                : ProjectCompiler.GetTargetAssembly(
                    options.DesignerProjectPath,
                    options.Configuration,
                    options.BuildProject);
            var model = DbContextModelLoader.Load(
                targetPath,
                designerPath,
                options.DesignerName,
                options.ContextName,
                options.GenerateSelectors,
                options.Profile);
            var bundle = RepositoryGenerator.Generate(model, options.Layout);
            if (options.ApiOutputPath is not null)
            {
                bundle = bundle.WithApiSnapshot();
            }

            if (options.ApiBaselinePath is not null)
            {
                if (string.Equals(options.ApiBaselinePath, options.ApiOutputPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("RGA0001: The approved baseline and candidate API output must be distinct files. Baselines are never automatically accepted.");
                }

                if (!File.Exists(options.ApiBaselinePath))
                {
                    throw new InvalidOperationException("RGA0001: API baseline file does not exist: " + options.ApiBaselinePath);
                }

                var changes = bundle.CreateApiSnapshot().CompareTo(RepositoryApiSnapshot.Parse(File.ReadAllText(options.ApiBaselinePath)), options.ApiCompatibilityMode);
                foreach (var change in changes)
                {
                    Console.Error.WriteLine($"{change.DiagnosticId}: {change.Contract}: {change.Member}: {change.Message} ({change.Impact})");
                }

                if (changes.Any(static change => change.IsBreaking))
                {
                    return 4; // No output writes, including in generation mode.
                }
            }

            var outputPath = options.OutputPath ?? Path.Combine(
                Path.GetDirectoryName(options.ProjectPath)!,
                "Generated",
                options.Layout != RepositoryOutputLayout.SingleFile ? "Repositories" : "RonSijm.RepoGen.Repositories.g.cs");
            if (model.Settings.ContractNamespace is not null)
            {
                if (!options.ValidateOnly && options.ContractsOutputPath is null)
                {
                    throw new InvalidOperationException(
                        "Closed repositories require --contracts-output pointing to the Application/Contracts project.");
                }

                if (options.ProjectionOutputPath is not null)
                {
                    throw new InvalidOperationException("Closed repositories include projection helpers in --output. Do not specify --projection-output in this mode.");
                }
            }
            else
            {
                if (options.ContractsOutputPath is not null)
                {
                    throw new InvalidOperationException("--contracts-output requires UseClosedRepositories in the repository designer.");
                }

                if (model.Entities.Any(static entity => entity.Projections.Count > 0 || entity.Queries.Count > 0))
                {
                    if (!options.ValidateOnly && options.ProjectionOutputPath is null)
                    {
                        throw new InvalidOperationException(
                            "The repository designer configured projections or named queries. Supply --projection-output so their service-facing methods can be generated.");
                    }
                }
            }

            if (options.TestDoublesOutputPath is not null && model.Settings.ScriptedTestDoubleNamespace is null)
            {
                throw new InvalidOperationException("--test-doubles-output requires UseScriptedTestDoubles in the repository designer.");
            }

            if (!options.ValidateOnly && bundle.Files.Any(static file => file.Kind == RepositoryOutputKind.TestDoubles) && options.TestDoublesOutputPath is null)
            {
                throw new InvalidOperationException("Scripted test doubles require --test-doubles-output pointing to a test-only project.");
            }

            if (options.ValidateOnly)
            {
                Console.WriteLine($"Validated {model.Entities.Count} repositories. No generated files were written.");
                return 0;
            }

            var locations = new RepositoryOutputLocations(outputPath, options.ContractsOutputPath, options.ProjectionOutputPath, options.ManifestOutputPath, options.ApiOutputPath, options.TestDoublesOutputPath);
            if (options.ApiBaselinePath is not null && bundle.GetOutputPaths(locations).Any(path => string.Equals(path, options.ApiBaselinePath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("RGA0001: An output destination overlaps the approved API baseline. Baselines are never rewritten by generation.");
            }

            if (options.CheckOnly)
            {
                var differences = bundle.Check(locations);
                foreach (var difference in differences)
                {
                    Console.Error.WriteLine($"{difference.DiagnosticId}: {difference.Path}: {difference.Message}");
                }

                if (differences.Count > 0)
                {
                    return 3;
                }

                Console.WriteLine("Generated outputs are current. No files were written.");
                return 0;
            }

            var paths = bundle.Write(locations);
            foreach (var obsolete in bundle.Check(locations).Where(static difference => difference.DiagnosticId == "RGG0004"))
            {
                Console.Error.WriteLine($"{obsolete.DiagnosticId}: {obsolete.Path}: {obsolete.Message}");
            }

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Generated {model.Entities.Count} repository/repositories from {model.ContextTypeName}."));
            foreach (var path in paths)
            {
                Console.WriteLine(path);
            }

            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            Console.Error.WriteLine("Repository generation failed:");
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("RonSijm.RepoGen repository generator");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  RonSijm.RepoGen.Tool.exe generate --project <project.csproj> [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --context <name>       DbContext type name when the project contains more than one.");
        writer.WriteLine("  --designer-project <project.csproj>  Project containing a RepositoryDesigner.");
        writer.WriteLine("  --designer <name>      RepositoryDesigner type name when the project contains more than one.");
        writer.WriteLine("  --output <file>        Generated C# file. Defaults to Generated/RonSijm.RepoGen.Repositories.g.cs.");
        writer.WriteLine("  --projection-output <file>  Generated projection extensions configured by the designer.");
        writer.WriteLine("  --contracts-output <file>   EF-free read/write ports for UseClosedRepositories.");
        writer.WriteLine("  --test-doubles-output <file> Test-only scripts/optional stores enabled by the designer; requires RonSijm.RepoGen.Testing.");
        writer.WriteLine("  --manifest-output <file>    Deterministic JSON inventory of generated operations.");
        writer.WriteLine("  --validate                 Validate and render without writing generated files.");
        writer.WriteLine("  --check                    Exit 3 for missing/stale output; never writes files.");
        writer.WriteLine("  --per-entity               Treat C# output locations as directories; emit one file per entity plus shared infrastructure.");
        writer.WriteLine("  --per-contract             Closed repositories: split persistence by entity and generated facades by interface.");
        writer.WriteLine("  --api-output <file>        Optional closed-contract API snapshot; a candidate, not an approved baseline.");
        writer.WriteLine("  --api-baseline <file>      Compare to an approved API snapshot; exit 4 for breaking changes before any writes.");
        writer.WriteLine("  --api-consumers-only       Allow new required members; otherwise handwritten implementers/mocks are checked too.");
        writer.WriteLine("  --configuration <name> Build configuration. Defaults to Debug.");
        writer.WriteLine("  --no-build             Inspect the existing project output without building first.");
        writer.WriteLine("  --generate-selectors   Always include entity selectors in the generated file.");
        writer.WriteLine("  --profile <name>       'simple' or 'compatibility' defaults; designer overrides run afterwards.");
        writer.WriteLine("  --help                 Show this help.");
    }
}
