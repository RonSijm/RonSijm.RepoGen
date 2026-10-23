using System.Linq.Expressions;
using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ProxyWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

[Collection("Proxy writes")]
public sealed class ProxyWriteGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void EveryLayoutUsesExplicitScalarWriteViewsWithoutConstructingPersistenceEntities(RepositoryOutputLayout layout)
    {
        using var context = Context(); var bundle = RepositoryGenerator.Generate(context, new ProxyWriteRepositoryDesigner(), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("MapWriteStore<").And.Contain("new global::Generated.ProxyWrites.TestDoubles.__RepoGenProxyWriteRecordReadRow")
            .And.NotContain("new global::RonSijm.RepoGen.Tool.Tests.ProxyWriteRecord").And.NotContain("Microsoft.EntityFrameworkCore");
        var model = RepositoryModelFactory.Create(context, typeof(ProxyWriteContext).Assembly, design: new ProxyWriteRepositoryDesigner().CreateModel());
        model.Entities.Should().OnlyContain(entity => entity.InMemoryStore!.WriteMapping != null && entity.Commands.All(command => command.InMemoryCommand!.Supported));
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("ExplicitScalarWriteAdapter").And.Contain("FullScalarAndPrimaryKeyValidation");
    }

    [Fact]
    public void DisableAndReadOnlyConfigurationsLeaveProductionContractsApiAndCacheShapesAlone()
    {
        using var context = Context(); var enabled = RepositoryGenerator.Generate(context, new ProxyWriteRepositoryDesigner());
        var disabled = RepositoryGenerator.Generate(context, new ProxyWriteRepositoryDesigner(commands: false));
        enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.TestDoubles and not RepositoryOutputKind.Manifest).Select(file => file.Contents)
            .Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.TestDoubles and not RepositoryOutputKind.Manifest).Select(file => file.Contents));
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        var all = RepositoryModelFactory.Create(context, typeof(ProxyWriteContext).Assembly, design: new ProxyWriteRepositoryDesigner().CreateModel());
        var off = RepositoryModelFactory.Create(context, typeof(ProxyWriteContext).Assembly, design: new ProxyWriteRepositoryDesigner(commands: false).CreateModel());
        JsonSerializer.Serialize(all.Entities.SelectMany(entity => entity.Commands)).Should().Be(JsonSerializer.Serialize(off.Entities.SelectMany(entity => entity.Commands)));
        ScriptedTestDoubleTestHarness.Compile(disabled);
        string.Join("\n", disabled.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents)).Should().NotContain("MapWriteStore<");
    }

    [Fact]
    public async Task CommandsCanBeEnabledWithoutAutomaticQueries()
    {
        using var context = Context(); var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new ProxyWriteRepositoryDesigner(reads: false)));
        var (repository, _, _, store) = Fixture(assembly);
        (await Invoke<RonSijm.RepoGen.Abstractions.CommandResult<ProxyWriteView>>(repository, "CreateAsync", new ProxyWriteRequest(0, "created"))).Value!.Version.Should().Be(7);
        store.Find(9)!.Caption.Should().Be("created");
    }

    [Fact]
    public void WriteAdapterPrerequisitesAndRemovalAreExplicit()
    {
        var builder = Builder(strict: false); var entity = builder.Entity<ProxyWriteRecord>();
        Action missing = () => entity.MapInMemoryWrites<ProxyWriteData>((row, saved) => new(row.Id, row.Name, row.Version, row.Label, row.Hidden, row.Stamp));
        missing.Should().Throw<InvalidOperationException>().WithMessage("RGD3601:*");
        ProxyWriteRepositoryDesigner.Store(builder);
        entity.WithoutInMemoryWrites(); var design = builder.Build().Entities.Single();
        design.InMemoryStore!.WriteMapping.Should().BeNull(); design.InMemoryStore.GeneratedValues.Should().BeEmpty();
        design.InMemoryStore.EntityMapping.Should().NotBeNull();
        ProxyWriteRepositoryDesigner.Store(builder); entity.WithoutInMemoryEntityMapping(); builder.Build().Entities.Single().InMemoryStore!.WriteMapping.Should().BeNull();
        ProxyWriteRepositoryDesigner.Store(builder); entity.WithoutInMemoryStore(); builder.Build().Entities.Single().InMemoryStore.Should().BeNull();
    }

    [Theory]
    [InlineData("Incomplete")]
    [InlineData("KeyTransform")]
    [InlineData("Clock")]
    [InlineData("Identity")]
    public void UnsafeOrIncompleteWriteDeclarationsFailBeforeEmittingCode(string scenario)
    {
        using var context = Context(); var builder = Builder(strict: false); var entity = ProxyWriteRepositoryDesigner.Store(builder);
        if (scenario == "Incomplete") entity.MapInMemoryEntity<ProxyWriteData>(row => new ProxyWriteRecord { Id = row.Key, Name = row.Caption });
        if (scenario == "KeyTransform") entity.MapInMemoryEntity<ProxyWriteData>(row => new ProxyWriteRecord
            { Id = row.Key + 1, Name = row.Caption, Version = row.Revision, Label = row.Label, Hidden = row.Archived, Stamp = row.Binary });
        if (scenario == "Clock") entity.MapInMemoryWrites<ProxyWriteData>((row, saved) => new(row.Id, row.Name, row.Version, DateTime.UtcNow.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Hidden, row.Stamp));
        if (scenario == "Identity") entity.MapInMemoryWrites<ProxyWriteData>((row, saved) => new(row.Id, row.ToString()!, row.Version, row.Label, row.Hidden, row.Stamp));
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3601:*");
    }

    [Fact]
    public void MissingConstructorAssignmentsRemainOverridesRatherThanInventedDomainDefaults()
    {
        using var context = Context(); var builder = Builder(strict: false); var entity = ProxyWriteRepositoryDesigner.Store(builder);
        var create = entity.HasCommand("Incomplete").Create<ProxyWriteRequest>(request => new ProxyWriteRecord { Id = request.Id }).AllowWrite(row => row.Id).Save();
        var coverage = RepositoryModelFactory.Create(context, typeof(ProxyWriteContext).Assembly, design: builder.Build()).Entities.Single().Commands.Single().InMemoryCommand!;
        coverage.Supported.Should().BeFalse(); coverage.Reasons.Should().Contain(reason => reason.Contains("explicitly assign scalar 'Name'", StringComparison.Ordinal));
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        builder.Configure(options => options.RequirePortableTestCommands());
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build()); strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        create.RequireTestOverride("An explicit script supplies this constructor-shaped operation.");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void PrivateModelWritesCompileWithoutAnyModelAssemblyReference()
    {
        using var context = Context(); var bundle = RepositoryGenerator.Generate(context, new ProxyWriteRepositoryDesigner());
        const string contracts = """
            namespace RonSijm.RepoGen.Tool.Tests;
            public sealed record ProxyWriteData(int Key, string Caption, int Revision, string Label, bool Archived, byte[] Binary, string Extra = "arranged");
            public sealed record ProxyWriteAuditData(int Key, string Caption);
            public sealed record ProxyWriteRequest(int Id, string Name, int ExpectedVersion = 3, string Label = null!, bool Supplied = true, byte[]? Stamp = null);
            public sealed record ProxyWriteView(int Id, string Name, int Version, string Label, byte[] Stamp);
            """;
        var trees = bundle.Files.Where(file => file.Kind is RepositoryOutputKind.Contracts or RepositoryOutputKind.TestDoubles)
            .Select(file => CSharpSyntaxTree.ParseText(file.Contents)).Append(CSharpSyntaxTree.ParseText(contracts));
        var compilation = CSharpCompilation.Create("ModelFreeProxyWrites", trees, ScriptedTestDoubleTestHarness.TestingReferences().Select(path => MetadataReference.CreateFromFile(path)),
            ScriptedTestDoubleTestHarness.CompilerOptions());
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
    }

    [Fact]
    public void RemovingOnlyTheWriteAdapterRetainsReadsAndRequiresExplicitCommandOverrides()
    {
        using var context = Context(); var builder = Builder(strict: false); ProxyWriteRepositoryDesigner.Configure(builder);
        builder.Entity<ProxyWriteRecord>().WithoutInMemoryWrites();
        var model = RepositoryModelFactory.Create(context, typeof(ProxyWriteContext).Assembly, design: builder.Build()).Entities.Single(entity => entity.EntityName == nameof(ProxyWriteRecord));
        model.Queries.Should().OnlyContain(query => query.InMemoryQuery!.Supported);
        model.Commands.Should().OnlyContain(command => !command.InMemoryCommand!.Supported);
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        builder.Configure(options => options.RequirePortableTestCommands());
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build()); strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Fact]
    public void GeneratedValueAdaptersCannotLeakExplicitInternalModelCasts()
    {
        using var context = Context(); var builder = Builder(); var entity = ProxyWriteRepositoryDesigner.Store(builder);
        entity.WithoutInMemoryGeneratedValue(row => row.Version)
            .HasInMemoryGeneratedValue(row => row.Version, (row, rows) => ((ProxyWriteRecord)(object)row).Name.Length);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3501:*");
    }

    [Fact]
    public void ReadOnlyEntityDoesNotRegisterUnusedWriteOrGeneratedValueAdapters()
    {
        using var context = Context(); var builder = Builder(); var entity = ProxyWriteRepositoryDesigner.Store(builder); entity.WriteEnabled = false;
        entity.HasQuery("List").Select(row => row.Name).List();
        var bundle = RepositoryGenerator.Generate(context, builder.Build()); ScriptedTestDoubleTestHarness.Compile(bundle);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().NotContain("MapWriteStore<").And.NotContain("ConfigureGeneratedValues<").And.Contain(" get; init; }");
        var store = RepositoryModelFactory.Create(context, typeof(ProxyWriteContext).Assembly, design: builder.Build()).Entities.Single().InMemoryStore!;
        store.WriteMapping.Should().BeNull(); store.GeneratedValues.Should().BeNull();
    }

    [Fact]
    public async Task RegisteredTypeAdapterMarkersExpandInsideWriteAndGeneratedValueDeclarations()
    {
        using var context = Context(); var builder = Builder(); ProxyWriteRepositoryDesigner.Configure(builder);
        var adapter = builder.HasTypeAdapter<int, int>(value => value + 0, value => value - 0);
        builder.Entity<ProxyWriteRecord>().MapInMemoryWrites<ProxyWriteData>((row, saved) => new(row.Id, row.Name,
                adapter.ToContract(row.Version), row.Label, row.Hidden, row.Stamp.ToArray(), saved == null ? "created" : saved.Extra))
            .WithoutInMemoryGeneratedValue(row => row.Version)
            .HasInMemoryGeneratedValue(row => row.Version, (row, rows) => adapter.ToContract(row.Name.Length));
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        var (repository, _, _, store) = Fixture(ScriptedTestDoubleTestHarness.Compile(bundle));
        (await Invoke<RonSijm.RepoGen.Abstractions.CommandResult<ProxyWriteView>>(repository, "CreateAsync", new ProxyWriteRequest(0, "new"))).Value!.Version.Should().Be(3);
        store.Find(9)!.Revision.Should().Be(3);
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().NotContain("RepositoryTypeAdapter");
    }

    [Fact]
    public void ScalarAdaptersDoNotSilentlySimulateDependentGraphDeletion()
    {
        using var context = new ProxyWriteGraphContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ProxyWriteGraphContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder(strict: false); var entity = ProxyWriteRepositoryDesigner.Store(builder);
        var command = entity.HasCommand("Remove").Where<int>((row, id) => row.Id == id).TrackedDelete().Save();
        var coverage = RepositoryModelFactory.Create(context, typeof(ProxyWriteGraphContext).Assembly, design: builder.Build()).Entities.Single().Commands.Single().InMemoryCommand!;
        coverage.Supported.Should().BeFalse(); coverage.Reasons.Should().Contain(reason => reason.Contains("dependent relationships", StringComparison.Ordinal));
        builder.Configure(options => options.RequirePortableTestCommands());
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build()); strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        command.RequireTestOverride("Use the real provider for relationship cascades.");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void SizedArrayAssignmentsRetainBoundsRatherThanBecomingArrayInitializers(int length)
    {
        Expression<Func<ProxyWriteRequest, byte[]>> factory = request => new byte[request.Id];
        ProjectionExpressionRenderer.RenderBody(factory, ["request"]).Should().Be("new global::System.Byte[request.Id]");
        factory.Compile()(new(length, "")).Should().HaveCount(length);
    }
}
