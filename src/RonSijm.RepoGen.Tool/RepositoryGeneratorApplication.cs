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
                options.GenerateSelectors);
            var source = RepositorySourceRenderer.Render(model);
            var outputPath = options.OutputPath ?? Path.Combine(
                Path.GetDirectoryName(options.ProjectPath)!,
                "Generated",
                "RonSijm.RepoGen.Repositories.g.cs");

            WriteIfChanged(outputPath, source);

            if (model.Entities.Any(static entity => entity.Projections.Count > 0 || entity.Queries.Count > 0))
            {
                if (options.ProjectionOutputPath is null)
                {
                    throw new InvalidOperationException(
                        "The repository designer configured projections or named queries. Supply --projection-output so their service-facing methods can be generated.");
                }

                WriteIfChanged(options.ProjectionOutputPath, ProjectionSourceRenderer.Render(model));
            }

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Generated {model.Entities.Count} repository/repositories from {model.ContextTypeName}."));
            Console.WriteLine(Path.GetFullPath(outputPath));
            if (options.ProjectionOutputPath is not null &&
                model.Entities.Any(static entity => entity.Projections.Count > 0 || entity.Queries.Count > 0))
            {
                Console.WriteLine(Path.GetFullPath(options.ProjectionOutputPath));
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

    private static void WriteIfChanged(string outputPath, string source)
    {
        outputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(outputPath)
                        ?? throw new InvalidOperationException("The output path does not have a parent directory.");
        Directory.CreateDirectory(directory);

        if (File.Exists(outputPath) && string.Equals(File.ReadAllText(outputPath), source, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(outputPath, source);
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
        writer.WriteLine("  --configuration <name> Build configuration. Defaults to Debug.");
        writer.WriteLine("  --no-build             Inspect the existing project output without building first.");
        writer.WriteLine("  --generate-selectors   Always include entity selectors in the generated file.");
        writer.WriteLine("  --help                 Show this help.");
    }
}