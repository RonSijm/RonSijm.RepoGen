using System.Reflection;
using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScriptedTestDoubleOutputTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void TestOutputParticipatesInOwnershipFreshnessAndAtomicPreflight(RepositoryOutputLayout layout)
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        using var temporary = new TemporaryOutput();
        var split = layout != RepositoryOutputLayout.SingleFile;
        var locations = Locations(temporary.Directory, split);
        var bundle = RepositoryGenerator.Generate(ScriptedTestDoubleTestHarness.Model(context), layout);
        bundle.Check(locations).Should().NotBeEmpty();
        bundle.Write(locations).Count.Should().Be(bundle.Files.Count);
        bundle.Check(locations).Should().BeEmpty();
        bundle.Write(locations).Should().BeEmpty();
        var testing = bundle.Resolve(locations).First(output => output.File.Kind == RepositoryOutputKind.TestDoubles);
        var previous = File.ReadAllText(testing.Path);
        File.WriteAllText(testing.Path, "// Handwritten test fixture\n");
        bundle.Check(locations).Should().Contain(difference => difference.Path == testing.Path && difference.DiagnosticId == "RGG0001");
        Action write = () => bundle.Write(locations);
        write.Should().Throw<InvalidOperationException>().WithMessage("RGG0003:*");
        File.WriteAllText(testing.Path, previous.Replace(bundle.Context, "global::Another.Context", StringComparison.Ordinal));
        write.Should().Throw<InvalidOperationException>().WithMessage("RGG0008:*");
        File.WriteAllText(testing.Path, previous);
        bundle.Check(locations).Should().BeEmpty();
    }

    [Fact]
    public void TestOutputRequiresItsOwnDestinationAndCannotOverwriteContracts()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        using var temporary = new TemporaryOutput();
        var locations = Locations(temporary.Directory);
        var bundle = RepositoryGenerator.Generate(ScriptedTestDoubleTestHarness.Model(context));
        Action missing = () => bundle.Write(locations with { TestDoubles = null });
        missing.Should().Throw<InvalidOperationException>().WithMessage("RGG0002:*TestDoubles*");
        Directory.GetFiles(temporary.Directory, "*", SearchOption.AllDirectories).Should().BeEmpty();
        Action overlapping = () => bundle.Write(locations with { TestDoubles = locations.Contracts });
        overlapping.Should().Throw<InvalidOperationException>().WithMessage("RGG0002:*");
        Directory.GetFiles(temporary.Directory, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void ExcludedSplitFixtureIsReportedAsStaleButNeverDeleted()
    {
        using var context = ScriptedTestDoubleTestHarness.Context();
        using var temporary = new TemporaryOutput();
        var builder = ScriptedTestDoubleTestHarness.Builder();
        builder.Entity<ScriptedRow>().HasQuery("Count").Count();
        builder.Entity<ScriptedOtherRow>().HasQuery("Count").Count();
        var locations = Locations(temporary.Directory, split: true);
        var original = RepositoryGenerator.Generate(context, builder.Build(), RepositoryOutputLayout.PerEntity);
        original.Write(locations);
        var obsolete = original.Resolve(locations).Single(output => output.File.Kind == RepositoryOutputKind.TestDoubles && output.File.Name.Contains("ScriptedOtherRow.", StringComparison.Ordinal)).Path;
        builder.Entity<ScriptedOtherRow>().GenerateTestDouble(false);
        var smaller = RepositoryGenerator.Generate(context, builder.Build(), RepositoryOutputLayout.PerEntity);
        smaller.Write(locations);
        smaller.Check(locations).Should().Contain(difference => difference.Path == obsolete && difference.DiagnosticId == "RGG0004");
        File.Exists(obsolete).Should().BeTrue();
    }

    [Fact]
    public void CliParsesTestOutputPathWithoutEnablingAnyGenerationOption()
    {
        GeneratorOptions.TryParse(["--project", ProjectPath, "--test-doubles-output", "TestDoubles.g.cs", "--validate"], out var options, out var error).Should().BeTrue();
        error.Should().BeNull();
        options!.TestDoublesOutputPath.Should().Be(Path.GetFullPath("TestDoubles.g.cs"));
        GeneratorOptions.TryParse(["--project", ProjectPath, "--test-doubles-output"], out _, out error).Should().BeFalse();
        error.Should().Contain("requires a value");
    }

    [Fact]
    public void CliValidationDoesNotRequireOutputButWritingDoes()
    {
        using var temporary = new TemporaryOutput();
        var args = Arguments(temporary.Directory).ToArray();
        RepositoryGeneratorApplication.Run(args.Append("--validate").ToArray()).Should().Be(0);
        Directory.GetFiles(temporary.Directory, "*", SearchOption.AllDirectories).Should().BeEmpty();
        RepositoryGeneratorApplication.Run(args).Should().Be(1);
        Directory.GetFiles(temporary.Directory, "*", SearchOption.AllDirectories).Should().BeEmpty();
        var withTesting = args.Concat(["--test-doubles-output", Path.Combine(temporary.Directory, "Testing/TestDoubles.g.cs")]).ToArray();
        RepositoryGeneratorApplication.Run(withTesting).Should().Be(0);
        RepositoryGeneratorApplication.Run(withTesting.Append("--check").ToArray()).Should().Be(0);
    }

    private static string ProjectPath => Path.GetFullPath("../../../RonSijm.RepoGen.Tool.Tests.csproj", AppContext.BaseDirectory);
    private static IEnumerable<string> Arguments(string directory) =>
    [
        "--project", ProjectPath, "--designer-project", ProjectPath, "--designer", nameof(ScriptedCliRepositoryDesigner),
        "--configuration", typeof(ScriptedContext).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration,
        "--context", nameof(ScriptedContext), "--no-build", "--output", Path.Combine(directory, "Persistence/Repositories.g.cs"),
        "--contracts-output", Path.Combine(directory, "Contracts/Contracts.g.cs")
    ];
    private static RepositoryOutputLocations Locations(string root, bool split = false) => new(
        Path.Combine(root, split ? "Persistence" : "Persistence/Repositories.g.cs"),
        Path.Combine(root, split ? "Contracts" : "Contracts/Repositories.g.cs"), Manifest: Path.Combine(root, "manifest.json"),
        TestDoubles: Path.Combine(root, split ? "Testing" : "Testing/TestDoubles.g.cs"));

    private sealed class TemporaryOutput : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "repogen-scripted-" + Guid.NewGuid().ToString("N"));
        internal TemporaryOutput() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true); // Only the owned, unique test directory above.
    }
}

public sealed class ScriptedCliRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Cli.Contracts")
            .UseScriptedTestDoubles("Cli.Testing").GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<ScriptedRow>().HasQuery("Count").Count();
    }
}
