using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedReconciliationTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReconciliationExecutionTests
{
    [Theory]
    [InlineData("Edit")]
    [InlineData("Current")]
    [InlineData("DeleteMissing")]
    public async Task RealSqliteAndFixtureReturnFreshParentAndChildMappedState(string operation)
    {
        await using var context = Context();
        await SeedAsync(context);
        var (memory, _, data) = Fixture();
        // Fixup may put a hidden child in the navigation, but it must not enter the write scope.
        await context.Set<MappedCollectionChild>().SingleAsync(child => child.Id == 3);
        var actual = await Call<CommandResult<MappedCollectionView>>(Operations(context), operation, Request());
        actual.Status.Should().Be(CommandStatus.Applied);
        (await Call<CommandResult<MappedCollectionView>>(memory, operation, Request())).Should().BeEquivalentTo(actual);
        var saved = actual.Value!;
        saved.Revision.Should().Be(4);
        saved.Items.Select(child => child.Revision).Should().Equal(4, 6);
        saved.Items.Select(child => child.Audit).Should().Equal(operation == "Current" ? [40, 40] : [11, 11]);
        saved.Items[0].Category.Should().Be(operation == "Current" ? "entry40" : "edited a");
        saved.Items[0].OptionalVersion.Should().Be(2);
        saved.Items[1].OptionalVersion.Should().BeNull();
        ChildStore(data).Find(3)!.Should().BeEquivalentTo(MappedCollectionTestHarness.Seeds()[2]);
        ChildStore(data).Find(4)!.Should().BeEquivalentTo(MappedCollectionTestHarness.Seeds()[3]);
        ParentStore(data).Find(1)!.Counter.Should().Be(3);
        (await Snapshots(memory)).Should().BeEquivalentTo(await Snapshots(Operations(context)));
        context.ChangeTracker.LazyLoadingEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("child")]
    [InlineData("nullable")]
    [InlineData("binary")]
    public async Task WrongOriginalTokensLeaveAllParticipantsUnchanged(string token)
    {
        await using var context = Context();
        await SeedAsync(context);
        var (memory, _, data) = Fixture();
        var request = Request();
        var item = request.Items[1];
        request = token == "parent" ? request with { Revision = 0 } : request with
        {
            Items = [request.Items[0], token == "child" ? item with { Revision = 0 } : token == "nullable" ? item with { OptionalVersion = 0 } : item with { Stamp = [9] }]
        };
        var actual = await Call<CommandResult<MappedCollectionView>>(Operations(context), "Edit", request);
        actual.Status.Should().Be(CommandStatus.Conflict);
        (await Call<CommandResult<MappedCollectionView>>(memory, "Edit", request)).Should().BeEquivalentTo(actual);
        data.PendingCount.Should().Be(0);
        ChildStore(data).Snapshot().Should().BeEquivalentTo(MappedCollectionTestHarness.Seeds());
        context.ChangeTracker.Entries().Where(entry => entry.State != EntityState.Unchanged).Should().BeEmpty();
        ParentStore(data).Find(1)!.Revision.Should().Be(3);
    }

    [Fact]
    public async Task StageDefersEveryMappedParticipantAndOneSharedSaveIncludesOtherRepositories()
    {
        await using var context = Context();
        await SeedAsync(context);
        var (memory, memoryChild, data) = Fixture();
        await Call<CommandResult>(Operations(context, child: true), "StageText", 4);
        await Call<CommandResult>(memoryChild, "StageText", 4);
        (await Call<CommandResult>(Operations(context), "Stage", Request())).Status.Should().Be(CommandStatus.Staged);
        (await Call<CommandResult>(memory, "Stage", Request())).Status.Should().Be(CommandStatus.Staged);
        ChildStore(data).Find(1)!.Audit.Should().Be(10);
        (await Snapshots(Operations(context)))[0].Revision.Should().Be(3);
        (await context.SaveChangesAsync()).Should().Be(4);
        (await data.SaveChangesAsync()).Should().Be(4);
        ChildStore(data).Find(4)!.Text.Should().Be("independent");
        ChildStore(data).Find(1)!.Audit.Should().Be(11);
        (await Snapshots(memory)).Should().BeEquivalentTo(await Snapshots(Operations(context)));
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task MappedParentAndChildAssignmentsPersistWithAutomaticDetectionDisabled()
    {
        await using var context = Context();
        await SeedAsync(context);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var (memory, _, _) = Fixture();
        var actual = await Call<CommandResult<MappedCollectionView>>(Operations(context), "Edit", Request());
        actual.Status.Should().Be(CommandStatus.Applied);
        actual.AffectedRows.Should().Be(3);
        (await Call<CommandResult<MappedCollectionView>>(memory, "Edit", Request())).Should().BeEquivalentTo(actual);
        context.ChangeTracker.AutoDetectChangesEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyKeepInputWithNoParentAssignmentsIsNoChanges()
    {
        await using var context = Context();
        await SeedAsync(context);
        var (memory, _, data) = Fixture();
        var request = Request() with { Items = [] };
        var actual = await Call<CommandResult<MappedCollectionView>>(Operations(context), "Conditional", request);
        actual.Status.Should().Be(CommandStatus.NoChanges);
        (await Call<CommandResult<MappedCollectionView>>(memory, "Conditional", request)).Should().BeEquivalentTo(actual);
        data.PendingCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConditionalMappedChildUpdatesWorkWithoutAnOriginalTokenOrAutomaticDetection(bool supplied)
    {
        await using var context = Context();
        await SeedAsync(context);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var (memory, _, data) = Fixture();
        var request = Request() with { Items = [Request().Items[0] with { Supplied = supplied }] };
        var actual = await Call<CommandResult<MappedCollectionView>>(Operations(context), "Conditional", request);
        actual.Status.Should().Be(supplied ? CommandStatus.Applied : CommandStatus.NoChanges);
        (await Call<CommandResult<MappedCollectionView>>(memory, "Conditional", request)).Should().BeEquivalentTo(actual);
        ChildStore(data).Find(1)!.Category.Should().Be(supplied ? "edited a" : "entry");
        ParentStore(data).Find(1)!.Revision.Should().Be(3);
        context.ChangeTracker.AutoDetectChangesEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("otherOwner")]
    [InlineData("duplicate")]
    [InlineData("tooMany")]
    [InlineData("rejectMissing")]
    public async Task ExistingBoundsOwnershipAndMissingPoliciesCannotPartiallyMutateMappedState(string scenario)
    {
        await using var context = Context();
        await SeedAsync(context);
        var (memory, _, data) = Fixture();
        var request = scenario switch
        {
            "hidden" => Request() with { Items = [new(3, "hidden", "no")] },
            "otherOwner" => new MappedReconciliationRequest(2, "no", [new(2, "b", "no", 5, null)], 5),
            "duplicate" => Request() with { Items = [Request().Items[0], Request().Items[0]] },
            "tooMany" => Request() with { Items = [new(1, "a", "a"), new(2, "b", "b"), new(3, "c", "c"), new(4, "d", "d")] },
            _ => Request() with { Items = [Request().Items[0]] }
        };
        foreach (var repository in new[] { Operations(context), memory })
        {
            Func<Task> invalid = () => Call<CommandResult<MappedCollectionView>>(repository, scenario == "rejectMissing" ? "RejectMissing" : "Edit", request);
            await invalid.Should().ThrowAsync<ArgumentException>();
        }
        data.PendingCount.Should().Be(0);
        ChildStore(data).Snapshot().Should().BeEquivalentTo(MappedCollectionTestHarness.Seeds());
        context.ChangeTracker.Entries().Where(entry => entry.State != EntityState.Unchanged).Should().BeEmpty();
        if (scenario is "duplicate" or "tooMany") context.ChangeTracker.Entries().Should().BeEmpty();
        ParentStore(data).Find(1)!.Revision.Should().Be(3);
    }

    [Fact]
    public async Task DeleteMissingRemovesOnlyVisibleOwnedRowsAndPreservesHiddenMappedValues()
    {
        await using var context = Context();
        await SeedAsync(context);
        var (memory, _, data) = Fixture();
        var request = Request() with { Items = [Request().Items[0]] };
        var actual = await Call<CommandResult<MappedCollectionView>>(Operations(context), "DeleteMissing", request);
        (await Call<CommandResult<MappedCollectionView>>(memory, "DeleteMissing", request)).Should().BeEquivalentTo(actual);
        actual.Value!.Items.Should().HaveCount(1);
        ChildStore(data).Find(2).Should().BeNull();
        ChildStore(data).Find(3)!.Text.Should().Be("hidden");
        (await context.Set<MappedCollectionChild>().Select(child => child.Id).OrderBy(id => id).ToListAsync()).Should().Equal(1, 3, 4);
    }

    [Fact]
    public async Task SkippedCollectionDoesNotEvaluateItsNullInputAndOnlyChangesTheParent()
    {
        await using var context = Context();
        await SeedAsync(context);
        var (memory, _, data) = Fixture();
        var request = Request() with { IncludeChildren = false, Items = null! };
        var actual = await Call<CommandResult<MappedCollectionView>>(Operations(context), "Edit", request);
        actual.Status.Should().Be(CommandStatus.Applied);
        actual.AffectedRows.Should().Be(1);
        (await Call<CommandResult<MappedCollectionView>>(memory, "Edit", request)).Should().BeEquivalentTo(actual);
        ChildStore(data).Snapshot().Should().BeEquivalentTo(MappedCollectionTestHarness.Seeds());
        ParentStore(data).Find(1)!.Revision.Should().Be(4);
    }

    [Fact]
    public async Task LaterMapperFailureRestoresAllMappedChildrenAndRetainsUnrelatedPendingWrites()
    {
        await using var context = Context();
        await SeedAsync(context);
        var (memory, memoryChild, data) = Fixture();
        await Call<CommandResult>(Operations(context, child: true), "StageText", 4);
        await Call<CommandResult>(memoryChild, "StageText", 4);
        foreach (var repository in new[] { Operations(context), memory })
        {
            Func<Task> failure = () => Call<CommandResult<MappedCollectionView>>(repository, "FailValue", Request());
            await failure.Should().ThrowAsync<DivideByZeroException>();
        }
        data.PendingCount.Should().Be(1);
        ChildStore(data).Snapshot().Should().BeEquivalentTo(MappedCollectionTestHarness.Seeds());
        context.ChangeTracker.Entries<MappedCollectionChild>().Where(entry => entry.Entity.ParentId == 1).Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        context.ChangeTracker.Entries<MappedCollectionChild>().Single(entry => entry.Entity.Id == 4).Entity.Text.Should().Be("independent");
    }

    [Fact]
    public async Task ProviderFailureRestoresParentAndChildrenIncludingEarlierDirtyParentValues()
    {
        await using var context = Context();
        await SeedAsync(context);
        var parent = await context.Set<MappedCollectionParent>().SingleAsync(row => row.Id == 1);
        parent.Title = "pending";
        var request = Request() with { Items = [Request().Items[0], Request().Items[1] with { Text = "invalid" }] };
        Func<Task> failure = () => Call<CommandResult<MappedCollectionView>>(Operations(context), "FailSave", request);
        await failure.Should().ThrowAsync<DbUpdateException>();
        context.Entry(parent).Property(row => row.Title).CurrentValue.Should().Be("pending");
        context.Entry(parent).Property(row => row.Title).OriginalValue.Should().Be("old");
        context.Entry(parent).Property(row => row.Title).IsModified.Should().BeTrue();
        context.Entry(parent).Property("Revision").CurrentValue.Should().Be(3);
        context.ChangeTracker.Entries<MappedCollectionChild>().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        (await context.Set<MappedCollectionChild>().OrderBy(child => child.Id).Select(child => EF.Property<int>(child, "audit")).ToListAsync()).Should().Equal(10, 10, 10, 10);
        (await context.Set<MappedCollectionParent>().Where(row => row.Id == 1).Select(row => row.Title).SingleAsync()).Should().Be("old");
    }

    [Fact]
    public async Task CancellationLeavesMappedParticipantStateUntouched()
    {
        await using var context = Context();
        await SeedAsync(context);
        var (memory, _, data) = Fixture();
        foreach (var repository in new[] { Operations(context), memory })
        {
            Func<Task> cancelled = () => (Task)repository.GetType().GetMethod("EditAsync")!.Invoke(repository, [Request(), new CancellationToken(true)])!;
            await cancelled.Should().ThrowAsync<OperationCanceledException>();
        }
        context.ChangeTracker.Entries().Should().BeEmpty();
        data.PendingCount.Should().Be(0);
        ChildStore(data).Snapshot().Should().BeEquivalentTo(MappedCollectionTestHarness.Seeds());
    }

    [Fact]
    public async Task FixtureCheckpointsAndBaselinesRestoreEveryMappedParticipant()
    {
        var (memory, _, data) = Fixture();
        data.CaptureBaseline();
        await using (await data.BeginTransactionAsync())
            await Call<CommandResult<MappedCollectionView>>(memory, "Edit", Request());
        ChildStore(data).Find(1)!.Audit.Should().Be(10);
        await Call<CommandResult<MappedCollectionView>>(memory, "Edit", Request());
        data.RestoreBaseline();
        ParentStore(data).Find(1)!.Revision.Should().Be(3);
        ChildStore(data).Snapshot().Should().BeEquivalentTo(MappedCollectionTestHarness.Seeds());
    }

    [Fact]
    public async Task MissingChildWriteStateRequiresAnExplicitScriptEvenWhenTheCollectionIsSkipped()
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context, Builder(childState: false, strict: false).Build());
        var (memory, _, data) = Fixture(ScriptedTestDoubleTestHarness.Compile(bundle));
        ChildStore(data).Clear();
        var request = Request() with { IncludeChildren = false, Items = [] };
        Func<Task> unsupported = () => Call<CommandResult<MappedCollectionView>>(memory, "Edit", request);
        await unsupported.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        var scripted = new CommandResult<MappedCollectionView>(CommandStatus.Applied, 42);
        ScriptedTestDoubleTestHarness.Returns(memory, "OnEditAsync", scripted);
        (await Call<CommandResult<MappedCollectionView>>(memory, "Edit", request)).Should().Be(scripted);
        data.PendingCount.Should().Be(0);
    }

    private static Task<IReadOnlyList<MappedCollectionView>> Snapshots(object repository) => Call<IReadOnlyList<MappedCollectionView>>(repository, "Snapshots");
}
