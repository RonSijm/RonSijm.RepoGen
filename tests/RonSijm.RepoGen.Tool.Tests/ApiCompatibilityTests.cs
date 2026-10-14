using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ApiCompatibilityTests
{
    [Fact]
    public void OptionalSnapshotsAreDeterministicLayoutIndependentAndPreserveBoundPortDefaultsAndNames()
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Api.Contracts").GenerateOnlyConfiguredEntities());
        builder.Entity<ClosedRow>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<IApiFixturePort>();
            entity.HasQuery("Count").Count();
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id).Count();
        });
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        bundle.Files.Should().NotContain(file => file.Kind == RepositoryOutputKind.ApiSnapshot);
        var snapshot = bundle.CreateApiSnapshot();
        var json = snapshot.ToJson();
        RepositoryApiSnapshot.Parse(json).ToJson().Should().Be(json);
        snapshot.CompareTo(RepositoryApiSnapshot.Parse(json)).Should().BeEmpty();
        var read = snapshot.Contracts.Single(contract => contract.Type == "global::Api.Contracts.IReadClosedRowRepository");
        read.Bases.Should().Contain("global::RonSijm.RepoGen.Tool.Tests.IApiFixtureParent");
        var find = read.Methods.Single(method => method.Signature.StartsWith("FindAsync(", StringComparison.Ordinal));
        find.Parameters[0].Name.Should().Be("identifier");
        find.Parameters[0].Optional.Should().BeTrue();
        find.Parameters[0].DefaultValue.Should().Be("7");
        find.Parameters[^1].Name.Should().Be("token");
        read.Methods.Single(method => method.Signature.StartsWith("CountAsync(", StringComparison.Ordinal)).Parameters[^1].Optional.Should().BeFalse();
        var helper = read.Methods.Single(method => method.Signature.StartsWith("FormatCount(", StringComparison.Ordinal));
        helper.RequiresImplementation.Should().BeFalse();
        helper.Parameters.Should().ContainSingle().Which.DefaultValue.Should().Be("0");
        RepositoryGenerator.Generate(context, builder.Build(), RepositoryOutputLayout.PerContract).CreateApiSnapshot().ToJson().Should().Be(json);
        bundle.WithApiSnapshot().WithApiSnapshot().Files.Should().ContainSingle(file => file.Kind == RepositoryOutputKind.ApiSnapshot);
    }

    [Theory]
    [InlineData("removed", "RGA1001")]
    [InlineData("input", "RGA1003")]
    [InlineData("return", "RGA1004")]
    [InlineData("nullable", "RGA1004")]
    [InlineData("parameter", "RGA1005")]
    [InlineData("optional", "RGA1006")]
    [InlineData("base", "RGA1002")]
    public void BreakingChangesHaveStableActionableDiagnostics(string change, string diagnostic)
    {
        var baseline = Baseline();
        var contract = baseline.Contracts.Single();
        var method = contract.Methods.Single();
        var parameters = method.Parameters.ToArray();
        switch (change)
        {
            case "input": method = method with { Signature = "FindAsync(global::System.Int32)" }; break;
            case "return": method = method with { ReturnType = "global::System.Threading.Tasks.Task<global::System.Int32>" }; break;
            case "nullable": parameters[0] = parameters[0] with { Type = "global::System.String?" }; break;
            case "parameter": parameters[0] = parameters[0] with { Name = "renamed" }; break;
            case "optional": parameters[^1] = parameters[^1] with { Optional = false, DefaultValue = null }; break;
        }

        var candidate = new RepositoryApiSnapshot(baseline.Context, change == "removed"
            ? []
            :
            [
                contract with
                {
                    Bases = change == "base" ? [] : contract.Bases, Methods = [method with { Parameters = parameters }]
                }
            ]);
        candidate.CompareTo(baseline).Should().Contain(difference => difference.DiagnosticId == diagnostic && difference.IsBreaking);
        candidate.CompareTo(baseline).Should().Equal(RepositoryApiSnapshot.Parse(candidate.ToJson()).CompareTo(baseline));
    }

    [Fact]
    public void ConsumerModeAllowsNewRequiredMethodsAndDefaultChangesAreAdvisories()
    {
        var baseline = Baseline();
        var contract = baseline.Contracts.Single();
        var method = contract.Methods.Single();
        var parameters = method.Parameters.ToArray();
        parameters[^1] = parameters[^1] with { DefaultValue = "different" };
        var candidate = new RepositoryApiSnapshot(baseline.Context, [contract with { Methods = [method with { Parameters = parameters }, method with { Signature = "ExtraAsync(global::System.String)" }] }]);
        candidate.CompareTo(baseline).Should().Contain(difference => difference.DiagnosticId == "RGA1007" && difference.IsBreaking);
        candidate.CompareTo(baseline, RepositoryApiCompatibilityMode.Consumers).Should().ContainSingle().Which.DiagnosticId.Should().Be("RGA1008");
        var overload = new RepositoryApiSnapshot(baseline.Context, [contract with { Methods = [method, method with { Signature = "FindAsync(global::Tests.Search)" }] }]);
        overload.CompareTo(baseline, RepositoryApiCompatibilityMode.Consumers).Should().ContainSingle().Which.DiagnosticId.Should().Be("RGA1009");
        var otherContext = new RepositoryApiSnapshot("global::Other.Context", baseline.Contracts);
        otherContext.CompareTo(baseline).Should().ContainSingle().Which.DiagnosticId.Should().Be("RGA0002");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2,\"generatedBy\":\"RonSijm.RepoGen\"}")]
    public void InvalidBaselinesAreRejected(string json)
    {
        Action parse = () => RepositoryApiSnapshot.Parse(json);
        parse.Should().Throw<ArgumentException>().WithMessage("*RGA0001*");
    }

    [Fact]
    public void PerContractOutputAndSnapshotsArePreflightedAndWrittenWithoutAcceptingABaseline()
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context, new ValidationFixtureDesigner(), RepositoryOutputLayout.PerContract).WithApiSnapshot();
        var root = Path.Combine(Path.GetTempPath(), "repogen-api-" + Guid.NewGuid().ToString("N"));
        var locations = new RepositoryOutputLocations(Path.Combine(root, "impl"), Path.Combine(root, "contracts"), Manifest: Path.Combine(root, "manifest.json"), ApiSnapshot: Path.Combine(root, "candidate.json"));
        try
        {
            bundle.Check(locations).Should().HaveCount(bundle.Files.Count);
            Directory.Exists(root).Should().BeFalse();
            var contractFiles = bundle.Files.Where(file => file.Kind == RepositoryOutputKind.Contracts).ToArray();
            contractFiles.Should().HaveCount(3);
            contractFiles.Should().OnlyContain(file => file.Contents.Split("public interface", StringSplitOptions.None).Length == 2);
            bundle.Write(locations).Should().HaveCount(bundle.Files.Count);
            bundle.Write(locations).Should().BeEmpty();
            bundle.Check(locations).Should().BeEmpty();
            RepositoryApiSnapshot.Parse(File.ReadAllText(locations.ApiSnapshot!)).CompareTo(bundle.CreateApiSnapshot()).Should().BeEmpty();
            File.WriteAllText(locations.ApiSnapshot!, "{\"applicationConfig\":true}");
            Action overwrite = () => bundle.Write(locations);
            overwrite.Should().Throw<InvalidOperationException>().WithMessage("*RGG0003*handwritten*");
            using var handwritten = JsonDocument.Parse(File.ReadAllText(locations.ApiSnapshot!));
            handwritten.RootElement.GetProperty("applicationConfig").GetBoolean().Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CliRejectsBreakingBaselineBeforeCreatingAnyOutputAndCannotOverwriteItsBaseline()
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context, new ValidationFixtureDesigner());
        var snapshot = bundle.CreateApiSnapshot();
        var read = snapshot.Contracts.Single(contract => contract.Type.Contains(".IRead", StringComparison.Ordinal));
        var baseline = new RepositoryApiSnapshot(snapshot.Context, snapshot.Contracts.Select(contract => contract == read
            ? contract with
            {
                Methods = [.. contract.Methods, new("RemovedAsync()", "global::System.Threading.Tasks.Task", [])]
            }
            : contract).ToArray());
        var root = Path.Combine(Path.GetTempPath(), "repogen-api-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "baseline.json");
        File.WriteAllText(file, baseline.ToJson());
        var output = Path.Combine(root, "missing", "repos.g.cs");
        var project = Path.GetFullPath("../../../RonSijm.RepoGen.Tool.Tests.csproj", AppContext.BaseDirectory);
        string[] args =
        [
            "--project", project, "--designer-project", project, "--designer", nameof(ValidationFixtureDesigner), "--context", nameof(ClosedContext),
            "--configuration", "Release", "--no-build", "--output", output, "--contracts-output", Path.Combine(root, "missing", "contracts.g.cs"), "--api-baseline", file
        ];
        try
        {
            RepositoryGeneratorApplication.Run(args).Should().Be(4);
            Directory.Exists(Path.GetDirectoryName(output)).Should().BeFalse();
            File.ReadAllText(file).Should().Be(baseline.ToJson());
            RepositoryGeneratorApplication.Run([.. args, "--api-output", file]).Should().Be(1);
            File.ReadAllText(file).Should().Be(baseline.ToJson());
            File.WriteAllText(file, snapshot.ToJson());
            RepositoryGeneratorApplication.Run([.. args, "--manifest-output", file]).Should().Be(1);
            File.ReadAllText(file).Should().Be(snapshot.ToJson());
            RepositoryGeneratorApplication.Run([.. args, "--validate"]).Should().Be(0);
            Directory.Exists(Path.GetDirectoryName(output)).Should().BeFalse();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("--api-consumers-only", "--api-baseline")]
    [InlineData("--per-entity", "--per-contract")]
    public void InvalidToolingSwitchCombinationsAreDiagnosed(string first, string second)
    {
        var project = Path.GetFullPath("../../../RonSijm.RepoGen.Tool.Tests.csproj", AppContext.BaseDirectory);
        var args = first == "--api-consumers-only" ? new[] { "--project", project, first } : ["--project", project, first, second];
        GeneratorOptions.TryParse(args, out _, out var error).Should().BeFalse();
        error.Should().Contain(second);
    }

    [Fact]
    public void ApiSnapshotsAndPerContractLayoutRemainExplicitClosedModeFeatures()
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context);
        Action snapshot = () => bundle.CreateApiSnapshot();
        snapshot.Should().Throw<InvalidOperationException>().WithMessage("*RGA0003*closed*");
        Action split = () => RepositoryGenerator.Generate(context, layout: RepositoryOutputLayout.PerContract);
        split.Should().Throw<InvalidOperationException>().WithMessage("*RGG0006*closed*");
    }

    private static ClosedContext Context() => new(new DbContextOptionsBuilder<ClosedContext>().UseSqlite("Data Source=:memory:").Options);

    private static RepositoryApiSnapshot Baseline() => new("global::Tests.Context", [
        new("global::Tests.IRead", ["global::Tests.IBase"],
        [
            new("FindAsync(global::System.String)", "global::System.Threading.Tasks.Task<global::System.String>",
                [new("term", "global::System.String"), new("cancellationToken", "global::System.Threading.CancellationToken", true, "default")])
        ])
    ]);
}

public interface IApiFixtureParent
{
    Task<int> CountAsync(CancellationToken token);
}

public interface IApiFixturePort : IApiFixtureParent
{
    Task<int> FindAsync(int identifier = 7, CancellationToken token = default);
    int FormatCount(int value = 0) => value;
}