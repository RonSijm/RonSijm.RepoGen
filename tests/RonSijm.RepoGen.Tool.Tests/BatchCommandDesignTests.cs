using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.BatchCommandTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class BatchCommandDesignTests
{
    [Fact]
    public void ContractsBindExactApplicationPortsAndKeepTheItemTypeBehindTheRequest()
    {
        using var context = CreateContext();
        var model = Model(context, ports: true);
        ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(Model(context));
        contracts.Should().Contain("CommandResult<global::System.Collections.Generic.IReadOnlyList<global::RonSijm.RepoGen.Tool.Tests.BatchRowView>>")
            .And.NotContain("IQueryable").And.NotContain("DbContext").And.NotContain("BatchItem item");
        using var manifest = JsonDocument.Parse(RepositoryManifestRenderer.Render(model));
        var command = manifest.RootElement.GetProperty("entities")[0].GetProperty("commands").EnumerateArray()
            .Single(command => command.GetProperty("name").GetString() == "RenameAsync");
        command.GetProperty("batch").GetProperty("maximumItems").GetInt32().Should().Be(10);
        command.GetProperty("batch").GetProperty("expectedVersions")[0].GetString().Should().Be("Version");
        model.Entities.Single().Commands.Single(command => command.MethodName == "RenameAsync").ReturnTypeName.Should().Contain("IReadOnlyList");
        RepositoryApiSnapshot.Capture(model).ToJson().Should().Contain("IReadOnlyList");
    }

    [Theory]
    [InlineData("compatibility")]
    [InlineData("cqrs")]
    [InlineData("noSave")]
    [InlineData("limit")]
    [InlineData("nonUnique")]
    [InlineData("computedKey")]
    [InlineData("keySetter")]
    [InlineData("protectedSetter")]
    [InlineData("duplicateSetter")]
    [InlineData("nonToken")]
    [InlineData("entityVersion")]
    [InlineData("conditionalOnly")]
    [InlineData("duplicateToken")]
    [InlineData("noSetter")]
    [InlineData("reload")]
    [InlineData("singleSetter")]
    [InlineData("singleVersion")]
    [InlineData("nativeResult")]
    [InlineData("navigation")]
    [InlineData("outcome")]
    [InlineData("wrongKind")]
    [InlineData("stageResult")]
    public void UnsafeOrMixedMappingsFailBeforePublishingGeneratedFiles(string scenario)
    {
        using var context = CreateContext();
        var builder = Builder(closed: scenario is not "compatibility" and not "cqrs");
        if (scenario == "cqrs") builder.Configure(options => options.UseCqrsSelectors());
        var entity = builder.Entity<BatchRow>();
        if (scenario == "protectedSetter") entity.ProtectWrite(row => row.Origin);
        var command = entity.HasCommand("Invalid").Where<BatchRequest>((row, request) => row.OwnerId == request.OwnerId);
        void Configure(RepositoryBatchItemBuilder<BatchRow, BatchItem> items)
        {
            if (scenario == "noSetter") return;
            if (scenario == "keySetter") items.SetProperty(row => row.Code, "Change matching key");
            else if (scenario == "protectedSetter") items.SetProperty(row => row.Origin, "Unapproved");
            else if (scenario == "navigation") items.SetProperty(row => row.Title, row => row.Owner.Name);
            else items.SetPropertyWhen(row => row.Title, (row, item) => item.Title, item => item.Supplied);
            if (scenario == "duplicateSetter") items.SetProperty(row => row.Title, "Repeated");
            if (scenario == "nonToken") items.ExpectVersion(row => row.Id, (row, item) => item.ExpectedVersion);
            if (scenario == "entityVersion") items.ExpectVersion(row => row.Version, (row, item) => row.Version);
            if (scenario is "conditionalOnly" or "duplicateToken") items.ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion);
            if (scenario == "duplicateToken") items.ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion);
        }

        command.PatchMany<BatchItem, string, BatchRequest>(request => request.Items,
            scenario == "nonUnique" ? row => row.Title : scenario == "computedKey" ? row => row.Code.ToUpperInvariant() : row => row.Code,
            item => item.Code, Configure, scenario == "limit" ? 0 : 10);
        if (scenario == "singleSetter") command.Patch(setters => setters.SetProperty(row => row.Title, "Wrong scope"));
        if (scenario == "singleVersion") command.ExpectVersion<int, BatchRequest>(row => row.Version, (row, request) => request.OwnerId);
        if (scenario == "reload") command.ReloadAfterSave();
        if (scenario == "outcome") command.WithOutcome();
        if (scenario == "wrongKind") command.Delete();
        if (scenario == "nativeResult") command.ReturnsMany(row => new BatchRowView(row.Code, row.Title, row.Version));
        if (scenario == "stageResult") command.Stage().Returns(row => new BatchRowView(row.Code, row.Title, row.Version));
        else if (scenario != "noSave") command.Save();
        Action generate = () => CreateModel(context, builder);
        generate.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BatchesCannotReplaceAnEarlierBatchOrDeleteACascadingGraph()
    {
        using var context = CreateContext();
        var builder = Builder();
        var command = builder.Entity<BatchRow>().HasCommand("Repeated")
            .PatchMany<BatchItem, string, BatchRequest>(request => request.Items, row => row.Code, item => item.Code, items => items.SetProperty(row => row.Title, "First"));
        Action repeated = () => command.PatchMany<BatchItem, string, BatchRequest>(request => request.Items, row => row.Code, item => item.Code, items => items.SetProperty(row => row.Title, "Second"));
        repeated.Should().Throw<InvalidOperationException>().WithMessage("RGD3001:*");
        using var protectedContext = new WriteProtectionContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<WriteProtectionContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var graph = Builder();
        graph.Entity<ProtectedNote>().HasCommand("DeleteGraph")
            .DeleteMany<BatchItem, string, BatchRequest>(request => request.Items, note => note.Code, item => item.Code, _ => { }).Save();
        Action generate = () => RepositoryModelFactory.Create(protectedContext, typeof(ProtectedNote).Assembly, design: graph.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3001:*leaf rows*");
    }

    [Fact]
    public void ExplicitProtectedPermissionsAreCommandLocalAndMatchingKeysRemainImmutable()
    {
        using var context = CreateContext();
        var builder = Builder();
        var entity = builder.Entity<BatchRow>().ProtectWrite(row => row.Origin);
        entity.HasCommand("Approved").PatchMany<BatchItem, string, BatchRequest>(request => request.Items, row => row.Code, item => item.Code,
            items => items.SetProperty(row => row.Origin, "Approved")).AllowWrite(row => row.Origin).Save();
        var approved = CreateModel(context, builder);
        ClosedRepositoryTests.Compile(approved, rejectWarnings: true);
        approved.Entities.Single().Commands.Single().AllowedProtectedWrites.Should().Equal("Origin");
        entity.HasCommand("Unapproved").PatchMany<BatchItem, string, BatchRequest>(request => request.Items, row => row.Code, item => item.Code,
            items => items.SetProperty(row => row.Origin, "Denied")).Save();
        Action generate = () => CreateModel(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2903:*");
        entity.ExcludeCommand("Unapproved");
        entity.HasCommand("MoveKey").PatchMany<BatchItem, string, BatchRequest>(request => request.Items, row => row.Code, item => item.Code,
            items => items.SetProperty(row => row.Code, "Cannot move")).AllowWrite(row => row.Code).Save();
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3002:*");
    }

    [Fact]
    public void RequestAliasesAndNullableRowResultsCompileWithoutGeneratedLocalCollisions()
    {
        using var context = CreateContext();
        var builder = Builder().Configure(options => options.UseGeneratedNamespace("Batch.Persistence"));
        builder.Entity<BatchRow>().HasCommand("Aliased")
            .Where<BatchRequest>((row, assigned) => row.OwnerId == assigned.OwnerId)
            .PatchMany<BatchItem, string, BatchRequest>(assigned => assigned.Items, row => row.Code, item => item.Code,
                items => items.SetProperty(row => row.Title, (row, item) => item.Title))
            .Save().Returns<BatchNullableRowView, BatchRequest>((row, assigned) => new BatchNullableRowView(row.Code, assigned.OwnerId == 1 ? row.Title : null));
        var model = CreateModel(context, builder);
        ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        model.Entities.Single().Commands.Single().Parameters.Should().ContainSingle().Which.Name.Should().Be("assigned");
    }

    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void AllOutputLayoutsAreDeterministicAndCompileStrictly(RepositoryOutputLayout layout)
    {
        using var context = CreateContext();
        var model = Model(context);
        var files = RepositoryGenerator.Generate(model, layout).Files;
        files.Should().Equal(RepositoryGenerator.Generate(model, layout).Files);
        var sources = files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts or RepositoryOutputKind.Projections)
            .Select(file => CSharpSyntaxTree.ParseText(file.Contents));
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(BatchCommandDesignTests).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create("BatchLayout_" + Guid.NewGuid().ToString("N"), sources,
            paths.Select(static path => MetadataReference.CreateFromFile(path)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        result.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning && diagnostic.Id != "CS1701")
            .Select(static diagnostic => diagnostic.ToString()).Should().BeEmpty();
    }
}
