using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedBatchTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedBatchGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void ExactNamedBatchTargetsCompileInEveryOutputLayout(RepositoryOutputLayout layout)
    {
        using var context = MappedSetterTestHarness.Context();
        var builder = Builder();
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        MappedSetterTestHarness.CompileProduction(context, builder, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("mappedBatchUpdateTargets").And.Contain("EntryCurrentValueUnderBoundedBatchSnapshotStageSave");
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.Repositories).Select(file => file.Contents));
        source.Should().Contain("context.Entry(entity).Property<global::System.String>(\"Caption\").CurrentValue");
        source.Should().NotContain("entity.Caption =");
        var contracts = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.Contracts)
            .SelectMany(file => file.Contents.Split('\n')).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        contracts.Should().NotContain("EntityFrameworkCore").And.NotContain("MappedSetterContext");
    }

    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void SqlServerCompilesWithoutOpeningAConnection(RepositoryOutputLayout layout)
    {
        using var context = new MappedSetterContext(new DbContextOptionsBuilder<MappedSetterContext>().UseSqlServer("Server=unused;Database=unused").Options);
        MappedSetterTestHarness.CompileProduction(context, Builder(), layout);
        context.Database.GetDbConnection().State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Fact]
    public void FixtureOptOutLeavesProductionAndContractUnchanged()
    {
        using var context = MappedSetterTestHarness.Context();
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(backend: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Contracts or RepositoryOutputKind.Repositories))
            disabled.Files.Single(other => other.Name == file.Name && other.Kind == file.Kind).Contents.Should().Be(file.Contents);
        disabled.CreateApiSnapshot().ToJson().Should().Be(enabled.CreateApiSnapshot().ToJson());
    }

    [Fact]
    public void IndependentCompleteFixtureWriteStateIsStillRequired()
    {
        using var context = MappedSetterTestHarness.Context();
        Action generate = () => RepositoryGenerator.Generate(context, Builder(state: false).Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        var model = RepositoryModelFactory.Create(context, typeof(MappedSetterContext).Assembly, design: Builder(state: false, strict: false).Build());
        var command = model.Entities.Single().Commands.Single(command => command.MethodName == "SaveBatchAsync");
        command.InMemoryCommand!.Supported.Should().BeFalse();
        command.InMemoryCommand.Reasons.Should().Contain(reason => reason.Contains("Named non-public/shadow/indexer setters", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("boxed")]
    [InlineData("widened")]
    [InlineData("nullable")]
    [InlineData("key")]
    [InlineData("foreignKey")]
    [InlineData("unique")]
    public void NamedBatchTargetsKeepExactTypesAndIdentityGuards(string mode)
    {
        using var context = MappedSetterTestHarness.Context();
        var builder = Builder();
        var command = builder.Entity<MappedSetterNote>().HasCommand("Invalid");
        command.PatchMany<MappedSetterRequest, int, IReadOnlyList<MappedSetterRequest>>(items => items, row => row.Id, item => item.Id, setters =>
        {
            switch (mode)
            {
                case "unknown": setters.SetProperty<int>("not mapped", 1); break;
                case "boxed": setters.SetProperty<object>(nameof(MappedSetterNote.Title), row => row.Title); break;
                case "widened": setters.SetProperty<long>("audit", 1); break;
                case "nullable": setters.SetProperty<int?>("audit", 1); break;
                case "key": setters.SetProperty<int>(nameof(MappedSetterNote.Id), 9); command.AllowWrite<int>(nameof(MappedSetterNote.Id)); break;
                case "foreignKey": setters.SetProperty<int?>(nameof(MappedSetterNote.OwnerId), 9); command.AllowWrite<int?>(nameof(MappedSetterNote.OwnerId)); break;
                default: setters.SetProperty<string>(nameof(MappedSetterNote.UniqueCode), "new"); command.AllowWrite<string>(nameof(MappedSetterNote.UniqueCode)); break;
            }
        }, maximumItems: 3).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1311:*");
    }

    [Fact]
    public void GeneratedTargetsAreNotEnabledByWritePermission()
    {
        using var context = MappedStateTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities().UseClosedRepositories("Generated.Invalid"));
        builder.Entity<MappedStateNote>().HasCommand("Invalid")
            .PatchMany<MappedSetterRequest, int, IReadOnlyList<MappedSetterRequest>>(items => items, row => row.Id, item => item.Id,
                setters => setters.SetProperty<int>("Revision", 7), maximumItems: 3).AllowWrite<int>("Revision").Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1311:*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtectionAndManagedTokensNeedTheirOwnWriterPermission(bool token)
    {
        using var context = MappedSetterTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Generated.MappedBatchPermissions"));
        var entity = builder.Entity<MappedSetterNote>();
        var name = token ? MappedSetterContext.Version : "audit";
        if (!token) entity.ProtectWrite<int>(name);
        var command = entity.HasCommand("Protected")
            .PatchMany<MappedSetterRequest, int, IReadOnlyList<MappedSetterRequest>>(items => items, row => row.Id, item => item.Id,
                setters => setters.SetProperty<int>(name, 7), maximumItems: 3).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>();
        command.AllowWrite<int>(name);
        MappedSetterTestHarness.CompileProduction(context, builder);
    }

    [Fact]
    public void MixedNamedAndMemberAssignmentsCannotRepeatOneProperty()
    {
        using var context = MappedSetterTestHarness.Context();
        var builder = Builder();
        builder.Entity<MappedSetterNote>().HasCommand("Invalid")
            .PatchMany<MappedSetterRequest, int, IReadOnlyList<MappedSetterRequest>>(items => items, row => row.Id, item => item.Id,
                setters => setters.SetProperty(row => row.Title, "one").SetProperty<string>(nameof(MappedSetterNote.Title), "two"), maximumItems: 3).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3002:*");
    }

    [Theory]
    [InlineData("dynamic")]
    [InlineData("receiver")]
    [InlineData("type")]
    [InlineData("dynamicIndexer")]
    [InlineData("foreignIndexer")]
    [InlineData("indexerType")]
    public void TrackedBatchMappedReadsAreStrictlyBound(string mode)
    {
        using var context = MappedSetterTestHarness.Context();
        var builder = Builder();
        builder.Entity<MappedSetterNote>().HasCommand("Invalid")
            .PatchMany<MappedSetterRequest, int, IReadOnlyList<MappedSetterRequest>>(items => items, row => row.Id, item => item.Id, setters =>
            {
                if (mode == "dynamic") setters.SetProperty<int>("audit", (row, item) => EF.Property<int>(row, item.Caption));
                else if (mode == "receiver") setters.SetProperty<int>("audit", row => EF.Property<int>(new MappedSetterNote(), "audit"));
                else if (mode == "type") setters.SetProperty<int>("audit", row => (int)EF.Property<long>(row, "audit"));
                else if (mode == "dynamicIndexer") setters.SetProperty<string?>(MappedSetterContext.Extra, (row, item) => (string?)row[item.Caption]);
                else if (mode == "foreignIndexer") setters.SetProperty<string?>(MappedSetterContext.Extra, row => (string?)new MappedSetterNote()[MappedSetterContext.Extra]);
                else setters.SetProperty<string?>(MappedSetterContext.Extra, row => (int)row[MappedSetterContext.Extra]! + "wrong type");
            }, maximumItems: 3).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1312:*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyBatchSetterNamesFailAtDeclaration(string? name)
    {
        var builder = new RepositoryModelBuilder().Entity<MappedSetterNote>().HasCommand("Invalid");
        Action configure = () => builder.PatchMany<MappedSetterRequest, int, IReadOnlyList<MappedSetterRequest>>(items => items, row => row.Id, item => item.Id,
            setters => setters.SetProperty<int>(name!, 1), maximumItems: 3);
        configure.Should().Throw<ArgumentException>().WithMessage("RGD1311:*");
    }

    [Fact]
    public void ExpectedVersionBatchRequiresAnUnconditionalSetter()
    {
        using var context = MappedSetterTestHarness.Context();
        var builder = Builder();
        builder.Entity<MappedSetterNote>().HasCommand("Invalid")
            .PatchMany<MappedSetterRequest, int, IReadOnlyList<MappedSetterRequest>>(items => items, row => row.Id, item => item.Id,
                setters => setters.SetPropertyWhen<string>(nameof(MappedSetterNote.Caption), (row, item) => item.Caption, item => item.ChangeCaption)
                    .ExpectVersion<int>(MappedSetterContext.Version, item => item.Version), maximumItems: 3).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3002:*");
    }

    [Fact]
    public void DeleteManyCannotGainMappedAssignments()
    {
        using var context = MappedSetterTestHarness.Context();
        var builder = Builder();
        builder.Entity<MappedSetterNote>().HasCommand("Invalid")
            .DeleteMany<MappedSetterRequest, int, IReadOnlyList<MappedSetterRequest>>(items => items, row => row.Id, item => item.Id,
                setters => setters.SetProperty<int>("audit", 7), maximumItems: 3).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3001:*");
    }
}
