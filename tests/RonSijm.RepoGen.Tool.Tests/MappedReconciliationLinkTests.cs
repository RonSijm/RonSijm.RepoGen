using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReconciliationLinkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedPayloadAndVersionSettersRetainSharedLinkOwnership(bool wrongOwner)
    {
        await using var context = SharedLinkTestHarness.Context();
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.AddRange(SharedLinkTestHarness.Owners());
        context.AddRange(SharedLinkTestHarness.Targets());
        context.AddRange(SharedLinkTestHarness.Links());
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = Builder();
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("ExplicitLinkRows");
        var production = NativeSavedTestHarness.CompileProduction(context, builder);
        var real = Activator.CreateInstance(production.GetType(SharedLinkTestHarness.Namespace + ".Persistence.SharedLinkOwnerRepositoryOperations")!, context)!;
        var (memory, data) = SharedLinkTestHarness.Repositories(assembly: ScriptedTestDoubleTestHarness.Compile(bundle));
        var request = new SharedLinkRequest(1, 1, [new(wrongOwner ? 2 : 1, 1, "mapped")]);
        if (wrongOwner)
        {
            foreach (var repository in new[] { real, memory })
            {
                Func<Task> invalid = () => MemoryWriteTestHarness.Invoke<CommandResult<SharedLinkView>>(repository, "MappedLinksAsync", request);
                await invalid.Should().ThrowAsync<ArgumentException>();
            }
            SharedLinkTestHarness.Rows(data, true).First().Note.Should().Be("A");
            data.PendingCount.Should().Be(0);
        }
        else
        {
            var actual = await MemoryWriteTestHarness.Invoke<CommandResult<SharedLinkView>>(real, "MappedLinksAsync", request);
            (await MemoryWriteTestHarness.Invoke<CommandResult<SharedLinkView>>(memory, "MappedLinksAsync", request)).Should().BeEquivalentTo(actual);
            actual.Status.Should().Be(CommandStatus.Applied);
            actual.Value!.Notes.Should().Equal("mapped", "B");
            SharedLinkTestHarness.Rows(data, true).First().Version.Should().Be(2);
            (await context.Set<SharedLinkTarget>().CountAsync()).Should().Be(5);
            (await context.Set<SharedLinkRow>().Where(row => row.Scope == 2).Select(row => row.Note).SingleAsync()).Should().Be("other");
        }
    }

    [Theory]
    [InlineData(nameof(SharedLinkRow.Scope))]
    [InlineData(nameof(SharedLinkRow.OwnerId))]
    [InlineData(nameof(SharedLinkRow.TargetId))]
    public void NamedLinkTargetsCannotChangeOwnershipOrReferenceIdentity(string name)
    {
        using var context = SharedLinkTestHarness.Context();
        var builder = Builder();
        builder.Entity<SharedLinkOwner>().HasCommand("Invalid")
            .ReconcileLinks<SharedLinkRow, SharedLinkItem, (int, int), SharedLinkRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId),
                links => links.AllowSharedReferenceColumns().MaximumItems(4).Update(setters => setters.SetProperty<int>(name, 7)).AllowWrite<int>(name)).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1311:*");
    }

    [Fact]
    public void NamedLinkValueReadsCannotTraverseEndpointGraphs()
    {
        using var context = SharedLinkTestHarness.Context();
        var builder = Builder();
        builder.Entity<SharedLinkOwner>().HasCommand("Invalid")
            .ReconcileLinks<SharedLinkRow, SharedLinkItem, (int, int), SharedLinkRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId),
                links => links.AllowSharedReferenceColumns().MaximumItems(4).Update(setters => setters.SetProperty<string>(nameof(SharedLinkRow.Note),
                    child => child.Target == null ? string.Empty : child.Target.Name)).AllowWrite<string>(nameof(SharedLinkRow.Note))).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1312:*");
    }

    private static RepositoryModelBuilder Builder()
    {
        var builder = SharedLinkTestHarness.Builder();
        var owner = builder.Entity<SharedLinkOwner>();
        foreach (var name in new[] { "Replace", "Merge", "Reject", "Stage", "Optional", "BadMap" }) owner.ExcludeCommand(name);
        builder.Entity<SharedLinkRow>().ProtectWrite<string>(nameof(SharedLinkRow.Note))
            .MapInMemoryEntity<SharedLinkRowData>(row => new SharedLinkRow { Scope = row.Scope, OwnerId = row.OwnerId, TargetId = row.TargetId, Hidden = row.Hidden })
            .MapInMemoryProperty<SharedLinkRowData, string>(nameof(SharedLinkRow.Note), row => row.Note)
            .MapInMemoryProperty<SharedLinkRowData, int>(nameof(SharedLinkRow.Version), row => row.Version)
            .MapInMemoryWrites<SharedLinkRowData>((row, saved) => new(row.Scope, row.OwnerId, row.TargetId, row.Note, row.Version, row.Hidden), useMappedPropertyState: true);
        owner.HasCommand("MappedLinks").Where<SharedLinkRequest>((row, request) => row.SpaceId == request.SpaceId && row.Id == request.OwnerId)
            .Patch(setters => setters.SetProperty<int>(nameof(SharedLinkOwner.Version), row => row.Version + 1))
            .AllowWrite<int>(nameof(SharedLinkOwner.Version)).ExpectVersion<int, SharedLinkRequest>(nameof(SharedLinkOwner.Version), request => request.ExpectedVersion)
            .ReconcileLinks<SharedLinkRow, SharedLinkItem, (int, int), SharedLinkRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId),
                links => links.AllowSharedReferenceColumns().MaximumItems(4).Update(setters => setters
                        .SetProperty<string>(nameof(SharedLinkRow.Note), (child, item) => item.Note)
                        .SetProperty<int>(nameof(SharedLinkRow.Version), child => child.Version + 1))
                    .AllowWrite<string>(nameof(SharedLinkRow.Note)).AllowWrite<int>(nameof(SharedLinkRow.Version))
                    .ExpectVersion<int>(nameof(SharedLinkRow.Version), item => item.ExpectedVersion))
            .Save().SimulateCollectionWrites().ReturnsSaved(row => new SharedLinkView(row.SpaceId, row.Id, row.Version,
                row.Links.Where(child => !child.Hidden).OrderBy(child => child.TargetId).Select(child => child.Note).ToList()));
        return builder;
    }
}
