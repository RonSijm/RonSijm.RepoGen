using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedSetterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedSetterGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void ExactMappedSettersCompileInAllLayouts(RepositoryOutputLayout layout)
    {
        using var context = Context();
        var builder = Builder();
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        CompileProduction(context, builder, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("mappedUpdateTargets").And.Contain("EntryCurrentValueUnderTrackedSnapshotStageSave");
        var contracts = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.Contracts)
            .SelectMany(file => file.Contents.Split('\n')).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        contracts.Should().NotContain("EntityFrameworkCore").And.NotContain("MappedSetterContext").And.NotContain("PropertyName");
    }

    [Fact]
    public void BackendOptOutDoesNotChangeProductionOrApi()
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(backend: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            disabled.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
    }

    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void SqlServerMappedSettersCompileWithoutOpeningAConnection(RepositoryOutputLayout layout)
    {
        using var context = new MappedSetterContext(new DbContextOptionsBuilder<MappedSetterContext>().UseSqlServer("Server=unused;Database=unused").Options);
        NativeSavedTestHarness.CompileProduction(context, Builder(), layout);
        context.Database.GetDbConnection().State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Fact]
    public void CompatibilityCommandSurfaceAlsoSupportsExplicitMappedTargets()
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        builder.Entity<MappedSetterNote>().HasCommand("Apply").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty<string>(nameof(MappedSetterNote.Caption), "updated")).Save();
        NativeSavedTestHarness.CompileProduction(context, builder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyDesignerNamesAreRejected(string? name)
    {
        var builder = new RepositoryModelBuilder().Entity<MappedSetterNote>();
        Action setter = () => builder.HasCommand("Invalid").Patch(setters => setters.SetProperty<int>(name!, 1));
        Action permission = () => builder.HasCommand("InvalidPermission").AllowWrite<int>(name!);
        Action protection = () => builder.ProtectWrite<int>(name!);
        setter.Should().Throw<ArgumentException>().WithMessage("RGD1311:*");
        permission.Should().Throw<ArgumentException>().WithMessage("RGD1311:*");
        protection.Should().Throw<ArgumentException>().WithMessage("RGD1311:*");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("boxed")]
    [InlineData("widened")]
    [InlineData("nullable")]
    [InlineData("permission")]
    public void MappedNamesRequireExactFinalModelScalarTypes(string mode)
    {
        using var context = Context();
        var builder = Builder();
        var command = builder.Entity<MappedSetterNote>().HasCommand("Invalid");
        command.Patch(setters =>
        {
            switch (mode)
            {
                case "unknown": setters.SetProperty<int>("not mapped", 1); break;
                case "boxed": setters.SetProperty<object>(nameof(MappedSetterNote.Title), row => row.Title); break;
                case "widened": setters.SetProperty<long>("audit", 1); break;
                case "nullable": setters.SetProperty<int?>("audit", 1); break;
                default: setters.SetProperty<int>("audit", 1); command.AllowWrite<string>("audit"); break;
            }
        }).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1311:*");
    }

    [Theory]
    [InlineData("key")]
    [InlineData("foreignKey")]
    [InlineData("unique")]
    public void MappedIdentityRemainsImmutableEvenWithPermission(string mode)
    {
        using var context = Context();
        var builder = Builder();
        var command = builder.Entity<MappedSetterNote>().HasCommand("Invalid");
        command.Patch(setters =>
        {
            if (mode == "key") { setters.SetProperty<int>(nameof(MappedSetterNote.Id), 7); command.AllowWrite<int>(nameof(MappedSetterNote.Id)); }
            else if (mode == "foreignKey") { setters.SetProperty<int?>(nameof(MappedSetterNote.OwnerId), 7); command.AllowWrite<int?>(nameof(MappedSetterNote.OwnerId)); }
            else { setters.SetProperty<string>(nameof(MappedSetterNote.UniqueCode), "new"); command.AllowWrite<string>(nameof(MappedSetterNote.UniqueCode)); }
        }).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1311:*");
    }

    [Fact]
    public void GeneratedColumnsRemainExcludedEvenWithPermission()
    {
        using var context = MappedStateTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities().UseClosedRepositories("Generated.Guard"));
        builder.Entity<MappedStateNote>().HasCommand("Invalid").Patch(setters => setters.SetProperty<int>("Revision", 2)).AllowWrite<int>("Revision").Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1311:*");
    }

    [Theory]
    [InlineData("returning")]
    [InlineData("atomic")]
    [InlineData("create")]
    [InlineData("delete")]
    [InlineData("batch")]
    public void MappedSettersDoNotBroadenOtherLifecycles(string mode)
    {
        using var context = Context();
        var builder = Builder();
        var command = builder.Entity<MappedSetterNote>().HasCommand("Invalid").Update(setters => setters.SetProperty<int>("audit", 1));
        switch (mode)
        {
            case "returning": command.ReturnsMany(row => row.Title); break;
            case "atomic": command.InsertIfAbsent(row => row.Id); break;
            case "create": command.Create<MappedSetterRequest>(request => new MappedSetterNote { Id = request.Id }).Save(); break;
            case "delete": command.Delete(); break;
            case "batch": command.PatchMany<MappedSetterRequest, int, IReadOnlyList<MappedSetterRequest>>(items => items, row => row.Id, item => item.Id,
                setters => setters.SetProperty(row => row.Title, (row, item) => item.Caption), maximumItems: 2).Save(); break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1311:*");
    }

    [Fact]
    public void NonPublicStateRequiresIndependentFixturePermission()
    {
        using var context = Context();
        Action strict = () => RepositoryGenerator.Generate(context, Builder(state: false).Build());
        strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        var model = RepositoryModelFactory.Create(context, typeof(MappedSetterContext).Assembly, design: Builder(state: false, strict: false).Build());
        model.Entities.Single().Commands.Single(command => command.MethodName == "ApplyAsync").InMemoryCommand!.Supported.Should().BeFalse();
    }

    [Fact]
    public void CallerAdvancedShadowTokensStillNeedExplicitWritePermission()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<MappedSetterNote>().HasCommand("Invalid").Patch(setters => setters.SetProperty<int>(MappedSetterContext.Version, 7)).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*protected property*");
    }

    [Fact]
    public void NamedPermissionCanSatisfyExplicitScalarProtection()
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities().UseClosedRepositories(Namespace + ".Contracts"));
        var entity = builder.Entity<MappedSetterNote>().ProtectWrite(row => row.Title);
        var command = entity.HasCommand("Protected").Patch(setters => setters.SetProperty<string>(nameof(MappedSetterNote.Title), "updated")).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2903:*");
        command.AllowWrite<string>(nameof(MappedSetterNote.Title));
        generate.Should().NotThrow();
    }

    [Fact]
    public void MixedNamedAndMemberTargetsCannotAssignOneScalarTwice()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<MappedSetterNote>().HasCommand("Invalid").Patch(setters => setters.SetProperty(row => row.Title, "one")
            .SetProperty<string>(nameof(MappedSetterNote.Title), "two")).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*same property more than once*");
    }

    [Fact]
    public void NamedShadowProtectionNeedsIndependentCommandPermission()
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities().UseClosedRepositories(Namespace + ".Contracts"));
        var entity = builder.Entity<MappedSetterNote>().ProtectWrite<int>("audit");
        var command = entity.HasCommand("Protected").Patch(setters => setters.SetProperty<int>("audit", 7)).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2903:*");
        command.AllowWrite<int>("audit");
        CompileProduction(context, builder);
    }

    [Fact]
    public void NamedProtectionChecksTheExactModelTypeBeforeCommands()
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities().UseClosedRepositories(Namespace + ".Contracts"));
        builder.Entity<MappedSetterNote>().ProtectWrite<string>("audit");
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1311:*");
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("replace")]
    [InlineData("member")]
    public void ProtectionRedeclarationDoesNotLeaveStaleTypedBindings(string mode)
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities().UseClosedRepositories(Namespace + ".Contracts"));
        var entity = builder.Entity<MappedSetterNote>();
        if (mode == "member")
            entity.ProtectWrite<int>(nameof(MappedSetterNote.Title)).ProtectWrite(row => row.Title);
        else
        {
            entity.ProtectWrite<string>("audit");
            if (mode == "disable") entity.ProtectWrite<string>("audit", false);
            else entity.ProtectWrite<int>("audit");
        }
        entity.HasCommand("Apply").Patch(setters => setters.SetProperty<int>("audit", 7)).AllowWrite<int>("audit").Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().NotThrow();
    }

    [Theory]
    [InlineData("dynamic")]
    [InlineData("receiver")]
    [InlineData("type")]
    [InlineData("foreignIndexer")]
    [InlineData("dynamicIndexer")]
    [InlineData("indexerType")]
    public void TrackedMappedValueReadsAreStrictlyBound(string mode)
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<MappedSetterNote>().HasCommand("Invalid").Patch(setters =>
        {
            if (mode == "dynamic") setters.SetProperty<int, MappedSetterRequest>("audit", (row, request) => EF.Property<int>(row, request.Caption));
            else if (mode == "receiver") setters.SetProperty<int>("audit", row => EF.Property<int>(new MappedSetterNote(), "audit"));
            else if (mode == "type") setters.SetProperty<int>("audit", row => (int)EF.Property<long>(row, "audit"));
            else if (mode == "foreignIndexer") setters.SetProperty<string?>(MappedSetterContext.Extra, row => (string?)new MappedSetterNote()[MappedSetterContext.Extra]);
            else if (mode == "dynamicIndexer") setters.SetProperty<string?, MappedSetterRequest>(MappedSetterContext.Extra, (row, request) => (string?)row[request.Caption]);
            else setters.SetProperty<string?>(MappedSetterContext.Extra, row => (int)row[MappedSetterContext.Extra]! + "wrong type");
        }).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1312:*");
    }
}
