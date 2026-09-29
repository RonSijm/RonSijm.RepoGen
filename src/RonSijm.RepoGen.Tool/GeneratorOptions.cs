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
    bool GenerateSelectors)
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
        var configuration = "Debug";
        var build = true;
        var generateSelectors = false;

        while (index < args.Count)
        {
            var argument = args[index++];
            switch (argument)
            {
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
                case "--generate-selectors":
                    generateSelectors = true;
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
            generateSelectors);
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