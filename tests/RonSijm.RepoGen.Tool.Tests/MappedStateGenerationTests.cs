using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedStateTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedStateGenerationTests
{
    [Fact]
    public void FixtureStateConsentDoesNotRelaxProductionAtomicShadowRestrictions()
    {
        using var context = Context();
        var builder = Builder().Configure(options => options.UseAtomicWrites<SqliteAtomicWriter>());
        builder.Entity<MappedStateNote>().HasCommand("Ensure")
            .Create<MappedStateRequest>(request => new MappedStateNote { Id = request.Id, Title = request.Title, Counter = 0 })
            .AllowWrite(row => row.Id).InsertIfAbsent(row => row.Id).SimulateImmediateWrites();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("Atomic mappings require*shadow-only*");
    }

    [Fact]
    public void ReadRelationshipPermissionDoesNotGrantMappedForeignKeyWriteState()
    {
        using var context = MappedKeyTestHarness.Context();
        var builder = MappedKeyTestHarness.Base().Configure(options => options.UseInMemoryTestCommands());
        var child = builder.Entity<MappedKeyChild>();
        child.WriteEnabled = true;
        child.MapInMemoryWrites<MappedKeyChildSeed>((row, saved) => new(row.Id, EF.Property<int?>(row, "OwnerScope"),
            (string?)row["OwnerCode"], row.Name, row.Deleted), useMappedPropertyState: true);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3601:*foreign keys*");
    }

    [Fact]
    public void DuplicateAndEmptyNamedFactoriesAreRejectedAtDesignTime()
    {
        var note = Builder().Entity<MappedStateNote>();
        Action duplicate = () => note.HasInMemoryGeneratedValue<int>("Revision", (row, rows) => 1);
        duplicate.Should().Throw<ArgumentException>().WithMessage("RGD3501:*");
        Action empty = () => note.HasInMemoryGeneratedValue<int>(" ", (row, rows) => 1);
        empty.Should().Throw<ArgumentException>().WithMessage("RGD3501:*");
    }

    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void EveryLayoutUsesTypedGetterFreeScalarStateAndExplicitReceipts(RepositoryOutputLayout layout)
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context, Builder().Build(), layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("MapWriteStore<").And.Contain("__repoGenProperty").And.NotContain("Microsoft.EntityFrameworkCore")
            .And.NotContain("new global::RonSijm.RepoGen.Tool.Tests.MappedStateNote").And.NotContain("__repoGenTarget.Caption").And.NotContain("__repoGenLeft.Revision");
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("mappedPropertyState").And.Contain("ExplicitFactoriesOrAssignmentsOnly")
            .And.Contain("FullStructuralRoundTripBeforeSharedCommit")
            .And.Contain("SeePerOperationMappedScalarStateCoverageOrTypedOverride")
            .And.NotContain("ShadowAndNonPublicTokenStateRequiresTypedOverride");
        var model = RepositoryModelFactory.Create(context, typeof(MappedStateContext).Assembly, design: Builder().Build());
        model.Entities.Single().Commands.Should().OnlyContain(command => command.InMemoryCommand!.Supported);
        model.Entities.Single().InMemoryStore!.WriteMapping!.MappedPropertyState.Should().HaveCount(5);
    }

    [Fact]
    public void ProductionContractsAndApiAreUnchangedAndDisabledCommandsIgnoreWriteState()
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(state: false, commands: false).Build());
        enabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents)
            .Should().Equal(disabled.Files.Where(file => file.Kind is not RepositoryOutputKind.Manifest and not RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        ScriptedTestDoubleTestHarness.Compile(disabled);
        string.Join("\n", disabled.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents)).Should().NotContain("MapWriteStore<");
    }

    [Fact]
    public async Task ReadsAreNotAPrerequisiteForExplicitScalarWriteState()
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context, Builder(queries: false).Build());
        var (repository, _, store) = Fixture(ScriptedTestDoubleTestHarness.Compile(bundle));
        (await Call<RonSijm.RepoGen.Abstractions.CommandResult<MappedStateCard>>(repository, "RenameAsync", new MappedStateRequest(1, "new")))
            .Value!.Revision.Should().Be(4);
        store.Find(1)!.Title.Should().Be("new");
    }

    [Fact]
    public void ReadBindingsAloneDoNotGrantWriteStateAndLifecycleResetsAreExplicit()
    {
        using var context = Context();
        var builder = Builder();
        var note = builder.Entity<MappedStateNote>();
        var store = builder.Build().Entities.Single().InMemoryStore!;
        store.MappedPropertyStateEnabled.Should().BeTrue();
        WriteMap(note, state: false);
        store.MappedPropertyStateEnabled.Should().BeFalse();
        foreach (var name in new[] { "Caption", "audit", "Revision", "OptionalVersion", "Stamp" })
        {
            note.WithoutInMemoryGeneratedValue(name);
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3601:*");
        WriteMap(note);
        note.WithoutInMemoryWrites();
        store.MappedPropertyStateEnabled.Should().BeFalse();
        store.GeneratedValues.Should().BeEmpty();
        store.PropertyMappings.Should().HaveCount(5);
        WriteMap(note);
        note.WithoutInMemoryEntityMapping();
        store.MappedPropertyStateEnabled.Should().BeFalse();
        store.PropertyMappings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("WrongType")]
    [InlineData("NonGenerated")]
    [InlineData("Clock")]
    public void NamedFactoriesRetainExactMetadataAndPurityValidation(string scenario)
    {
        using var context = Context();
        var builder = Builder(strict: false);
        var note = builder.Entity<MappedStateNote>();
        note.WithoutInMemoryGeneratedValue("Revision");
        if (scenario == "Missing")
        {
            note.HasInMemoryGeneratedValue<int>("NotMapped", (row, rows) => 1);
        }
        else if (scenario == "WrongType")
        {
            note.HasInMemoryGeneratedValue<long>("Revision", (row, rows) => 1L);
        }
        else if (scenario == "NonGenerated")
        {
            note.HasInMemoryGeneratedValue<string>(nameof(MappedStateNote.Title), (row, rows) => "not-generated");
        }
        else
        {
            note.HasInMemoryGeneratedValue<int>("Revision", (row, rows) => DateTime.UtcNow.Second);
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3501:*");
    }

    [Fact]
    public void MappedPrimaryKeysRemainOutsideScalarWriteState()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<MappedStateNote>().MapInMemoryEntity<MappedStateSeed>(row => new MappedStateNote { Title = row.Title, Counter = row.Counter })
            .MapInMemoryProperty<MappedStateSeed, int>(nameof(MappedStateNote.Id), row => row.Id);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3601:*keys*");
    }
}
