using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedPropertyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedPropertyVersionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetBasedAndReturningCommandsConsumeTheCallersShadowToken(bool returning)
    {
        await using var fixture = await CreateAsync(); var builder = Builder(); var command = Versioned(builder, "Rename");
        if (returning) command.ReturnsMany(row => EF.Property<int>(row, MappedPropertyContext.Token)).IncludeShadowProperties();
        var operations = Operations(fixture.Context, builder);
        async Task<int> Invoke(MappedPropertyInput input) => returning
            ? (await ReturningWriteTests.Call<Task<MutationResult<int>>>(operations, "RenameAsync", input, CancellationToken.None)).Items.SingleOrDefault()
            : await ReturningWriteTests.Call<Task<int>>(operations, "RenameAsync", input, CancellationToken.None);
        (await Invoke(new(1, 99))).Should().Be(0);
        (await Invoke(new(3, 1))).Should().Be(0); // global visibility is retained
        (await Invoke(new(1, 1))).Should().Be(returning ? 2 : 1);
        (await Invoke(new(1, 1))).Should().Be(0);
        (await fixture.Context.Notes.AsNoTracking().Where(row => row.Id == 1).Select(row => EF.Property<int>(row, MappedPropertyContext.Token)).SingleAsync()).Should().Be(2);
        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TrackedCommandsCheckOriginalShadowStateAndReturnFreshSavedTokens(bool stage, bool noTrackingContext)
    {
        await using var fixture = await CreateAsync(); var builder = Builder(); var command = Versioned(builder, "Rename", tracked: true, stage: stage);
        if (noTrackingContext) fixture.Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        if (!stage) command.ReturnsSaved(row => EF.Property<int>(row, MappedPropertyContext.Token));
        var operations = Operations(fixture.Context, builder);
        if (stage)
        {
            var wrong = await ReturningWriteTests.Call<Task<CommandResult>>(operations, "RenameAsync", new MappedPropertyInput(1, 99), CancellationToken.None);
            wrong.Status.Should().Be(CommandStatus.Conflict);
            var result = await ReturningWriteTests.Call<Task<CommandResult>>(operations, "RenameAsync", new MappedPropertyInput(1), CancellationToken.None);
            result.Status.Should().Be(CommandStatus.Staged);
            (await fixture.Context.Notes.AsNoTracking().Where(row => row.Id == 1).Select(row => row.Counter).SingleAsync()).Should().Be(0);
            await fixture.Context.SaveChangesAsync();
        }
        else
        {
            (await ReturningWriteTests.Call<Task<CommandResult<int>>>(operations, "RenameAsync", new MappedPropertyInput(1, 99), CancellationToken.None)).Status.Should().Be(CommandStatus.Conflict);
            var result = await ReturningWriteTests.Call<Task<CommandResult<int>>>(operations, "RenameAsync", new MappedPropertyInput(1), CancellationToken.None);
            result.Status.Should().Be(CommandStatus.Applied); result.Value.Should().Be(2);
            (await ReturningWriteTests.Call<Task<CommandResult<int>>>(operations, "RenameAsync", new MappedPropertyInput(1), CancellationToken.None)).Status.Should().Be(CommandStatus.Conflict);
        }
        (await fixture.Context.Notes.AsNoTracking().Where(row => row.Id == 1).Select(row => EF.Property<int>(row, MappedPropertyContext.Token)).SingleAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrackedDeletionChecksShadowVersionsWithoutSavingAStagedRemoval(bool stage)
    {
        await using var fixture = await CreateAsync(); var builder = Builder();
        fixture.Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var command = builder.Entity<MappedPropertyNote>().HasCommand("Remove")
            .Where<MappedPropertyInput>((row, input) => row.Id == input.Id).TrackedDelete()
            .ExpectVersion<int, MappedPropertyInput>(MappedPropertyContext.Token, input => input.Version);
        if (stage) command.Stage(); else command.Save();
        var operations = Operations(fixture.Context, builder);
        (await ReturningWriteTests.Call<Task<CommandResult>>(operations, "RemoveAsync", new MappedPropertyInput(1, 99), CancellationToken.None))
            .Status.Should().Be(CommandStatus.Conflict);
        var result = await ReturningWriteTests.Call<Task<CommandResult>>(operations, "RemoveAsync", new MappedPropertyInput(1), CancellationToken.None);
        result.Status.Should().Be(stage ? CommandStatus.Staged : CommandStatus.Applied);
        if (stage)
        {
            (await fixture.Context.Notes.AsNoTracking().AnyAsync(row => row.Id == 1)).Should().BeTrue();
            await fixture.Context.SaveChangesAsync();
        }
        (await fixture.Context.Notes.AsNoTracking().AnyAsync(row => row.Id == 1)).Should().BeFalse();
    }

    [Theory]
    [InlineData("nullable")]
    [InlineData("binary")]
    [InlineData("converted")]
    public async Task ExactNullableBinaryAndConvertedModelTokenTypesParticipateInPredicates(string kind)
    {
        await using var fixture = await CreateAsync(); var builder = Builder();
        var command = builder.Entity<MappedPropertyNote>().HasCommand("Delete").Where<MappedPropertyInput>((row, input) => row.Id == input.Id).Delete();
        if (kind == "nullable") command.ExpectVersion<int?, MappedPropertyInput>("OptionalVersion", input => input.OptionalVersion);
        else if (kind == "binary") command.ExpectVersion<byte[]?, MappedPropertyInput>("BinaryVersion", input => input.BinaryVersion);
        else command.ExpectVersion<ReturningState, MappedPropertyInput>("State", input => input.State);
        var operations = Operations(fixture.Context, builder);
        var wrong = new MappedPropertyInput(1, OptionalVersion: 88, BinaryVersion: [9], State: ReturningState.Pending);
        (await ReturningWriteTests.Call<Task<int>>(operations, "DeleteAsync", wrong, CancellationToken.None)).Should().Be(0);
        var correct = new MappedPropertyInput(1, BinaryVersion: [1, 2], State: ReturningState.Ready);
        (await ReturningWriteTests.Call<Task<int>>(operations, "DeleteAsync", correct, CancellationToken.None)).Should().Be(1);
        (await ReturningWriteTests.Call<Task<int>>(operations, "DeleteAsync", correct, CancellationToken.None)).Should().Be(0);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("wrongType")]
    [InlineData("notToken")]
    [InlineData("duplicate")]
    [InlineData("create")]
    public void InvalidTokenBindingsFailDuringGeneration(string kind)
    {
        using var context = Empty(); var builder = Builder(); var command = builder.Entity<MappedPropertyNote>().HasCommand("Change");
        if (kind == "create") command.Create<MappedPropertyInput>(input => new MappedPropertyNote { Id = input.Id, Title = input.Title });
        else command.Delete();
        if (kind == "unknown") command.ExpectVersion<int, MappedPropertyInput>("Missing", input => input.Version);
        else if (kind == "wrongType") command.ExpectVersion<long, MappedPropertyInput>(MappedPropertyContext.Token, input => input.Version);
        else if (kind == "notToken") command.ExpectVersion<int, MappedPropertyInput>(nameof(MappedPropertyNote.Counter), input => input.Version);
        else command.ExpectVersion<int, MappedPropertyInput>(MappedPropertyContext.Token, input => input.Version);
        if (kind == "duplicate") command.ExpectVersion<int, MappedPropertyInput>(MappedPropertyContext.Token, input => input.Version);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>();
        context.Database.GetDbConnection().State.Should().Be(System.Data.ConnectionState.Closed);
    }
}
