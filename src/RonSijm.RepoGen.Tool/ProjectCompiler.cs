using System.Diagnostics;
using System.Text.Json;

namespace RonSijm.RepoGen.Tool;

internal static class ProjectCompiler
{
    public static string GetTargetAssembly(GeneratorOptions options)
        => GetTargetAssembly(options.ProjectPath, options.Configuration, options.BuildProject);

    public static string GetTargetAssembly(string projectPath, string configuration, bool buildProject)
    {
        if (buildProject)
        {
            RunDotNet(
                ["build", projectPath, "--configuration", configuration, "--nologo"],
                captureOutput: false);
        }

        var query = RunDotNet(
            [
                "msbuild",
                projectPath,
                "-getProperty:TargetPath",
                $"-property:Configuration={configuration}",
                "-nologo"
            ],
            captureOutput: true);

        var targetPath = ReadTargetPath(query);

        if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
        {
            throw new InvalidOperationException(
                $"MSBuild reported target assembly '{targetPath}', but that file does not exist.");
        }

        return Path.GetFullPath(targetPath);
    }

    private static string? ReadTargetPath(string query)
    {
        var trimmedQuery = query.Trim();
        if (!trimmedQuery.StartsWith('{'))
        {
            return trimmedQuery;
        }

        using var document = JsonDocument.Parse(trimmedQuery);
        return document.RootElement
            .GetProperty("Properties")
            .GetProperty("TargetPath")
            .GetString();
    }

    private static string RunDotNet(IReadOnlyList<string> arguments, bool captureOutput)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Unable to start the dotnet CLI.");
        var standardOutput = captureOutput ? process.StandardOutput.ReadToEnd() : string.Empty;
        var standardError = captureOutput ? process.StandardError.ReadToEnd() : string.Empty;
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.{Environment.NewLine}{standardError}");
        }

        return standardOutput;
    }
}