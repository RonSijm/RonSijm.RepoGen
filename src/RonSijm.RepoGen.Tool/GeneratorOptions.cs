using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal sealed record GeneratorOptions(
    string ProjectPath,
    string? DesignerProjectPath,
    string? DesignerName,
    string? ContextName,
    string? OutputPath,
    string? ProjectionOutputPath,
    string Configuration,
    bool BuildProject,
    bool GenerateSelectors,
    string? ContractsOutputPath = null,
    bool ValidateOnly = false,
    string? ManifestOutputPath = null,
    RepositoryGenerationProfile? Profile = null,
    bool CheckOnly = false,
    RepositoryOutputLayout Layout = RepositoryOutputLayout.SingleFile,
    string? ApiOutputPath = null,
    string? ApiBaselinePath = null,
    RepositoryApiCompatibilityMode ApiCompatibilityMode = RepositoryApiCompatibilityMode.ConsumersAndImplementers,
    string? TestDoublesOutputPath = null)
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        out GeneratorOptions? options,
        out string? error)
    {
        options = null;
        error = null;

        if (args.Count == 0 || args.Any(static value => value is "--help" or "-h"))
        {
            return false;
        }

        var index = string.Equals(args[0], "generate", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        string? project = null;
        string? designerProject = null;
        string? designer = null;
        string? context = null;
        string? output = null;
        string? projectionOutput = null;
        string? contractsOutput = null;
        string? testDoublesOutput = null;
        string? manifestOutput = null;
        string? apiOutput = null;
        string? apiBaseline = null;
        var apiConsumersOnly = false;
        var validateOnly = false;
        var checkOnly = false;
        var layout = RepositoryOutputLayout.SingleFile;
        var configuration = "Debug";
        var build = true;
        var generateSelectors = false;
        RepositoryGenerationProfile? profile = null;

        while (index < args.Count)
        {
            var argument = args[index++];
            switch (argument)
            {
                case "--check": checkOnly = true; break;
                case "--per-entity":
                case "--per-contract":
                    if (layout != RepositoryOutputLayout.SingleFile)
                    {
                        error = "Specify just one output layout (--per-entity or --per-contract).";
                        return false;
                    }

                    layout = argument == "--per-entity" ? RepositoryOutputLayout.PerEntity : RepositoryOutputLayout.PerContract;
                    break;
                case "--api-output":
                    if (!TryReadValue(args, ref index, argument, out apiOutput, out error))
                    {
                        return false;
                    }

                    break;
                case "--api-baseline":
                    if (!TryReadValue(args, ref index, argument, out apiBaseline, out error))
                    {
                        return false;
                    }

                    break;
                case "--api-consumers-only": apiConsumersOnly = true; break;
                case "--project":
                    if (!TryReadValue(args, ref index, argument, out project, out error))
                    {
                        return false;
                    }

                    break;
                case "--context":
                    if (!TryReadValue(args, ref index, argument, out context, out error))
                    {
                        return false;
                    }

                    break;
                case "--designer-project":
                    if (!TryReadValue(args, ref index, argument, out designerProject, out error))
                    {
                        return false;
                    }

                    break;
                case "--designer":
                    if (!TryReadValue(args, ref index, argument, out designer, out error))
                    {
                        return false;
                    }

                    break;
                case "--output":
                    if (!TryReadValue(args, ref index, argument, out output, out error))
                    {
                        return false;
                    }

                    break;
                case "--configuration":
                    if (!TryReadValue(args, ref index, argument, out var configuredValue, out error))
                    {
                        return false;
                    }

                    configuration = configuredValue!;
                    break;
                case "--projection-output":
                    if (!TryReadValue(args, ref index, argument, out projectionOutput, out error))
                    {
                        return false;
                    }

                    break;
                case "--no-build":
                    build = false;
                    break;
                case "--contracts-output":
                    if (!TryReadValue(args, ref index, argument, out contractsOutput, out error))
                    {
                        return false;
                    }

                    break;
                case "--manifest-output":
                    if (!TryReadValue(args, ref index, argument, out manifestOutput, out error))
                    {
                        return false;
                    }

                    break;
                case "--test-doubles-output":
                    if (!TryReadValue(args, ref index, argument, out testDoublesOutput, out error))
                    {
                        return false;
                    }

                    break;
                case "--validate":
                    validateOnly = true;
                    break;
                case "--generate-selectors":
                    generateSelectors = true;
                    break;
                case "--profile":
                    if (!TryReadValue(args, ref index, argument, out var profileName, out error))
                    {
                        return false;
                    }

                    profile = profileName?.ToLowerInvariant() switch
                    {
                        "simple" => RepositoryGenerationProfile.Simple,
                        "compatibility" => RepositoryGenerationProfile.Compatibility,
                        _ => null
                    };
                    if (profile is null)
                    {
                        error = "--profile must be 'simple' or 'compatibility'.";
                        return false;
                    }

                    break;
                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(project))
        {
            error = "--project is required.";
            return false;
        }

        if (validateOnly && checkOnly)
        {
            error = "--check and --validate are mutually exclusive.";
            return false;
        }

        if (apiConsumersOnly && apiBaseline is null)
        {
            error = "--api-consumers-only requires --api-baseline.";
            return false;
        }

        project = Path.GetFullPath(project);
        if (!File.Exists(project))
        {
            error = $"Project file does not exist: {project}";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(designerProject))
        {
            designerProject = Path.GetFullPath(designerProject);
            if (!File.Exists(designerProject))
            {
                error = $"Designer project file does not exist: {designerProject}";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(projectionOutput) && string.IsNullOrWhiteSpace(designerProject))
        {
            error = "--projection-output requires --designer-project.";
            return false;
        }

        options = new GeneratorOptions(
            project,
            designerProject,
            designer,
            context,
            string.IsNullOrWhiteSpace(output) ? null : Path.GetFullPath(output),
            string.IsNullOrWhiteSpace(projectionOutput) ? null : Path.GetFullPath(projectionOutput),
            configuration,
            build,
            generateSelectors,
            string.IsNullOrWhiteSpace(contractsOutput) ? null : Path.GetFullPath(contractsOutput),
            validateOnly,
            string.IsNullOrWhiteSpace(manifestOutput) ? null : Path.GetFullPath(manifestOutput),
            profile, checkOnly, layout,
            apiOutput is null ? null : Path.GetFullPath(apiOutput), apiBaseline is null ? null : Path.GetFullPath(apiBaseline),
            apiConsumersOnly ? RepositoryApiCompatibilityMode.Consumers : RepositoryApiCompatibilityMode.ConsumersAndImplementers,
            string.IsNullOrWhiteSpace(testDoublesOutput) ? null : Path.GetFullPath(testDoublesOutput));
        return true;
    }

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string argument,
        out string? value,
        out string? error)
    {
        if (index >= args.Count || args[index].StartsWith("--", StringComparison.Ordinal))
        {
            value = null;
            error = $"{argument} requires a value.";
            return false;
        }

        value = args[index++];
        error = null;
        return true;
    }
}
