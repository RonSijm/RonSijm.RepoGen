using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ImmediateTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryCommandTranslationNativeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImmediateSettersReadOriginalSavedRowsAndTranslatedVersionsDoNotRefreshPendingWrites(bool mapped)
    {
        var builder = Builder(mapped); InMemoryCommandTranslationTestHarness.Register(builder);
        var entity = builder.Entity<ImmediateRow>();
        entity.HasCommand("TranslatedBump").Where<ImmediateRequest>((row, request) => row.OwnerId == TranslationCalls.Echo(request.OwnerId) && EF.Functions.Like(row.Code, request.Code))
            .Update(setters => setters.SetProperty<int, ImmediateRequest>(row => row.Amount, (row, request) => TranslationCalls.Shift(row.Amount, request.Delta))
                .SetProperty(row => row.Previous, row => TranslationCalls.Echo(row.Amount)).SetProperty(row => row.Version, row => TranslationCalls.Shift(row.Version, 1)))
            .AllowWrite(row => row.Version).ExpectVersion<int, ImmediateRequest>(row => row.Version, (row, request) => TranslationCalls.Echo(request.ExpectedVersion))
            .ReturnsMany(row => new ImmediateView(row.Id, TranslatedWriteCalls.Normalize(row.Code), row.Amount, row.Previous, row.Version, row.Stamp.ToArray()))
            .SimulateImmediateWrites().UseInMemoryTranslations();
        entity.HasCommand("TranslatedConsume").Where<ImmediateRequest>((row, request) => row.OwnerId == request.OwnerId && EF.Functions.Like(row.Code, request.Code))
            .Delete().ExpectVersion<int, ImmediateRequest>(row => row.Version, (row, request) => TranslationCalls.Echo(request.ExpectedVersion))
            .ReturnsMany(row => TranslationCalls.Measure(row.Code)).SimulateImmediateWrites().UseInMemoryTranslations();
        var assembly = Compile(builder);
        using var context = Context(); CompileProduction(context, builder);
        var (_, repository, data) = Fixture(mapped, assembly);
        await Invoke<CommandResult>(repository, "StageCodeAsync", new ImmediateRequest(1, "staged", 1));
        var update = await Invoke<MutationResult<ImmediateView>>(repository, "TranslatedBumpAsync", new ImmediateRequest(1, "%ON%", 4));
        update.Items.Should().ContainSingle(); update.Items[0].Code.Should().Be("ONE"); update.Items[0].Amount.Should().Be(6);
        update.Items[0].Previous.Should().Be(2); update.Items[0].Version.Should().Be(2);
        var stale = await Invoke<MutationResult<int>>(repository, "TranslatedConsumeAsync", new ImmediateRequest(1, "%ON%"));
        stale.AffectedRows.Should().Be(0); data.PendingCount.Should().Be(1);
        Func<Task> save = () => data.SaveChangesAsync(); await save.Should().ThrowAsync<RepositoryWriteConflictException>();
        data.DiscardChanges();
        await using (await data.BeginTransactionAsync())
        {
            var deleted = await Invoke<MutationResult<int>>(repository, "TranslatedConsumeAsync", new ImmediateRequest(1, "%ON%", ExpectedVersion: 2));
            deleted.Items.Should().Equal(3);
        }
        (mapped ? MappedStore(data).Count : EntityStore(data).Count).Should().Be(4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtomicCreateConflictTargetsSettersAndReturnValuesRetainTheirDistinctBranches(bool mapped)
    {
        var builder = AtomicFixtureTestHarness.Builder(mapped); InMemoryCommandTranslationTestHarness.Register(builder);
        var entity = builder.Entity<ImmediateRow>();
        foreach (var upsert in new[] { false, true })
        {
            var command = entity.HasCommand(upsert ? "TranslatedUpsert" : "TranslatedEnsure")
                .Create<AtomicFixtureInput>(request => new ImmediateRow
                {
                    OwnerId = TranslationCalls.Echo(request.OwnerId), Code = TranslatedWriteCalls.Normalize(request.Code), Amount = TranslationCalls.Echo(request.Amount),
                    Previous = TranslationCalls.Echo(0), Version = TranslationCalls.Echo(1), Stamp = request.Stamp ?? new byte[] { 1 }, Hidden = TranslationCalls.Echo(request.Hidden)
                }).AllowWrite(row => row.Version).AllowWrite(row => row.Stamp);
            if (upsert) command.Where<AtomicFixtureInput>((row, request) => TranslationCalls.Positive(row.Amount))
                .InsertOrUpdate(row => new { row.OwnerId, row.Code }, setters => setters
                    .SetProperty<int, AtomicFixtureInput>(row => row.Amount, (row, request) => TranslationCalls.Shift(row.Amount, request.Amount))
                    .SetProperty(row => row.Previous, row => TranslationCalls.Echo(row.Amount)).SetProperty(row => row.Version, row => TranslationCalls.Shift(row.Version, 1)))
                .ExpectVersion<int, AtomicFixtureInput>(row => row.Version, (row, request) => TranslationCalls.Echo(request.ExpectedVersion));
            else command.InsertIfAbsent(row => new { row.OwnerId, row.Code });
            command.Returns(row => new ImmediateView(row.Id, row.Code, row.Amount, row.Previous, TranslationCalls.Echo(row.Version), row.Stamp.ToArray()))
                .SimulateImmediateWrites().UseInMemoryTranslations();
        }
        using var context = AtomicFixtureTestHarness.Context(); AtomicFixtureTestHarness.CompileProduction(context, builder);
        var assembly = AtomicFixtureTestHarness.Compile(builder); var (_, repository, data) = AtomicFixtureTestHarness.Fixture(mapped, assembly);
        var created = await Invoke<CommandResult<ImmediateView>>(repository, "TranslatedEnsureAsync", new AtomicFixtureInput(1, " next "));
        created.Status.Should().Be(CommandStatus.Inserted); created.Value!.Code.Should().Be("NEXT"); created.Value.Id.Should().Be(5);
        (await Invoke<CommandResult<ImmediateView>>(repository, "TranslatedEnsureAsync", new AtomicFixtureInput(1, "next"))).Status.Should().Be(CommandStatus.AlreadyExists);
        var updated = await Invoke<CommandResult<ImmediateView>>(repository, "TranslatedUpsertAsync", new AtomicFixtureInput(1, "next", Amount: 3));
        updated.Status.Should().Be(CommandStatus.Updated); updated.Value!.Amount.Should().Be(5); updated.Value.Previous.Should().Be(2); updated.Value.Version.Should().Be(2);
        (await Invoke<CommandResult<ImmediateView>>(repository, "TranslatedUpsertAsync", new AtomicFixtureInput(1, "next"))).Status.Should().Be(CommandStatus.NotMatched);
        data.PendingCount.Should().Be(0);
    }
}
