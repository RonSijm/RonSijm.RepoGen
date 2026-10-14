using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class GeneratorValidationTests
{
    [Fact]
    public void ParsesNewOutputAndValidationOptions()
    {
        GeneratorOptions.TryParse(["--project", ProjectPath, "--no-build", "--validate", "--contracts-output", "contracts.g.cs", "--manifest-output", "manifest.json"], out var options, out var error).Should().BeTrue();
        error.Should().BeNull();
        options!.ValidateOnly.Should().BeTrue();
        options.ContractsOutputPath.Should().EndWith("contracts.g.cs");
        options.ManifestOutputPath.Should().EndWith("manifest.json");
    }

    [Fact]
    public void ValidationRendersClosedOperationsButDoesNotCreateOutputFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "repogen-validate-" + Guid.NewGuid().ToString("N"));
        RepositoryGeneratorApplication.Run(Arguments(directory).Append("--validate").ToArray()).Should().Be(0);
        Directory.Exists(directory).Should().BeFalse();
    }

    [Fact]
    public void DuplicateOutputsFailBeforeCreatingAnyFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "repogen-paths-" + Guid.NewGuid().ToString("N"));
        var args = Arguments(directory).ToArray();
        args[Array.IndexOf(args, "--contracts-output") + 1] = args[Array.IndexOf(args, "--output") + 1];
        RepositoryGeneratorApplication.Run(args).Should().Be(1);
        Directory.Exists(directory).Should().BeFalse();
    }

    private static string ProjectPath => Path.GetFullPath("../../../RonSijm.RepoGen.Tool.Tests.csproj", AppContext.BaseDirectory);

    private static IEnumerable<string> Arguments(string directory) =>
    [
        "--project", ProjectPath, "--designer-project", ProjectPath, "--designer", nameof(ValidationFixtureDesigner),
        "--context", nameof(ClosedContext), "--no-build", "--output", Path.Combine(directory, "repositories.g.cs"),
        "--contracts-output", Path.Combine(directory, "contracts.g.cs"), "--manifest-output", Path.Combine(directory, "manifest.json")
    ];
}

public sealed class ValidationFixtureDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseClosedRepositories("Generated.Contracts").GenerateOnlyConfiguredEntities().GenerateCacheDecorator(false));
        modelBuilder.Entity<ClosedRow>().HasQuery("Count").Count();
    }
}