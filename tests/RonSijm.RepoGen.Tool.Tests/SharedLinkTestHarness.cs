using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class SharedLinkTestHarness
{
    internal const string Namespace = "Generated.SharedLinks";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    internal static SharedLinkContext Context() => new(new DbContextOptionsBuilder<SharedLinkContext>().UseSqlite("Data Source=:memory:").Options);
    private static Assembly Compile(bool mapped)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool shared = true, bool simulate = true, bool strict = true, bool backend = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".TestDoubles").UseInMemoryTestStores().UseInMemoryTestQueries()
            .UseInMemoryTestCommands(backend).RequirePortableTestQueries().RequirePortableTestCommands(strict && backend));
        var owner = builder.Entity<SharedLinkOwner>(); var target = builder.Entity<SharedLinkTarget>(); var link = builder.Entity<SharedLinkRow>();
        target.WriteEnabled = false;
        if (mapped)
        {
            owner.HasInMemoryStore<SharedLinkOwnerData, (int, int)>(row => new ValueTuple<int, int>(row.SpaceId, row.Id), row => new(row.SpaceId, row.Id, row.Name, row.Version, row.Hidden))
                .MapInMemoryEntity<SharedLinkOwnerData>(row => new SharedLinkOwner { SpaceId = row.SpaceId, Id = row.Id, Name = row.Name, Version = row.Version, Hidden = row.Hidden })
                .MapInMemoryWrites<SharedLinkOwnerData>((row, saved) => new(row.SpaceId, row.Id, row.Name, row.Version, row.Hidden));
            target.HasInMemoryStore<SharedLinkTargetData, (int, int)>(row => new ValueTuple<int, int>(row.PartitionId, row.Id), row => new(row.PartitionId, row.Id, row.Name))
                .MapInMemoryEntity<SharedLinkTargetData>(row => new SharedLinkTarget { PartitionId = row.PartitionId, Id = row.Id, Name = row.Name });
            link.HasInMemoryStore<SharedLinkRowData, (int, int, int)>(row => new ValueTuple<int, int, int>(row.Scope, row.OwnerId, row.TargetId), row => new(row.Scope, row.OwnerId, row.TargetId, row.Note, row.Version, row.Hidden))
                .MapInMemoryEntity<SharedLinkRowData>(row => new SharedLinkRow { Scope = row.Scope, OwnerId = row.OwnerId, TargetId = row.TargetId, Note = row.Note, Version = row.Version, Hidden = row.Hidden })
                .MapInMemoryWrites<SharedLinkRowData>((row, saved) => new(row.Scope, row.OwnerId, row.TargetId, row.Note, row.Version, row.Hidden));
        }
        else
        {
            owner.HasInMemoryStore(row => new ValueTuple<int, int>(row.SpaceId, row.Id), row => new SharedLinkOwner { SpaceId = row.SpaceId, Id = row.Id, Name = row.Name, Version = row.Version, Hidden = row.Hidden });
            target.HasInMemoryStore(row => new ValueTuple<int, int>(row.PartitionId, row.Id), row => new SharedLinkTarget { PartitionId = row.PartitionId, Id = row.Id, Name = row.Name });
            link.HasInMemoryStore(row => new ValueTuple<int, int, int>(row.Scope, row.OwnerId, row.TargetId), row => new SharedLinkRow { Scope = row.Scope, OwnerId = row.OwnerId, TargetId = row.TargetId, Note = row.Note, Version = row.Version, Hidden = row.Hidden });
        }
        owner.HasVisibilityPolicy("Visible", row => !row.Hidden); link.HasVisibilityPolicy("Visible", row => !row.Hidden);
        owner.HasInMemoryNavigation(row => row.Links);
        owner.HasQuery("Find").Where<SharedLinkRequest>((row, request) => row.SpaceId == request.SpaceId && row.Id == request.OwnerId)
            .Select(row => new SharedLinkView(row.SpaceId, row.Id, row.Version, row.Links.Where(child => !child.Hidden).OrderBy(child => child.TargetId).Select(child => child.Note).ToList())).SingleOrDefault();
        foreach (var name in new[] { "Replace", "Merge", "Reject", "Stage" })
        {
            var command = owner.HasCommand(name).Where<SharedLinkRequest>((row, request) => row.SpaceId == request.SpaceId && row.Id == request.OwnerId)
                .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                .ExpectVersion<int, SharedLinkRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
                .ReconcileLinks<SharedLinkRow, SharedLinkItem, (int, int), SharedLinkRequest>(row => row.Links, request => request.Items!,
                    row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId),
                    links => links.AllowSharedReferenceColumns(shared).MaximumItems(4)
                        .Add(item => new SharedLinkRow { Scope = item.Scope, TargetId = item.TargetId, Note = item.Note, Version = 1, Hidden = false })
                        .Update(setters => setters.SetProperty(row => row.Note, (row, item) => item.Note).SetProperty(row => row.Version, row => row.Version + 1))
                        .AllowWrite(row => row.Version).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                        .MissingItems(name == "Merge" ? RepositoryCollectionRemovalBehavior.Keep : name == "Reject" ? RepositoryCollectionRemovalBehavior.Reject : RepositoryCollectionRemovalBehavior.Delete))
                .SimulateCollectionWrites(simulate);
            if (name == "Stage") command.Stage();
            else command.Save().ReturnsSaved(row => new SharedLinkView(row.SpaceId, row.Id, row.Version, row.Links.Where(child => !child.Hidden).OrderBy(child => child.TargetId).Select(child => child.Note).ToList()));
        }
        owner.HasCommand("Optional").Where<SharedLinkRequest>((row, request) => row.SpaceId == request.SpaceId && row.Id == request.OwnerId)
            .ReconcileLinks<SharedLinkRow, SharedLinkItem, (int, int), SharedLinkRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId),
                links => links.AllowSharedReferenceColumns(shared).MaximumItems(4).When<SharedLinkRequest>(request => request.Apply).MissingItems(RepositoryCollectionRemovalBehavior.Delete))
            .Save().SimulateCollectionWrites(simulate);
        owner.HasCommand("BadMap").Where<SharedLinkRequest>((row, request) => row.SpaceId == request.SpaceId && row.Id == request.OwnerId)
            .ReconcileLinks<SharedLinkRow, SharedLinkItem, (int, int), SharedLinkRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId),
                links => links.AllowSharedReferenceColumns(shared).Add(item => new SharedLinkRow { Scope = item.Scope + 1, TargetId = item.TargetId, Note = item.Note, Version = 1, Hidden = false }).AllowWrite(row => row.Version))
            .Save().SimulateCollectionWrites(simulate);
        owner.HasCommand("StageName").Where<int>((row, space) => row.SpaceId == space).Patch(setters => setters.SetProperty(row => row.Name, "pending")).Stage();
        return builder;
    }

    internal static (object Owner, InMemoryWriteDataContext Data) Repositories(bool mapped = true)
    {
        var assembly = mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.SharedLinkContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped)
        {
            Store<SharedLinkOwnerData, (int, int)>(data, nameof(SharedLinkOwner)).SeedRange(Owners().Select(row => new SharedLinkOwnerData(row.SpaceId, row.Id, row.Name, row.Version, row.Hidden)));
            Store<SharedLinkTargetData, (int, int)>(data, nameof(SharedLinkTarget)).SeedRange(Targets().Select(row => new SharedLinkTargetData(row.PartitionId, row.Id, row.Name)));
            Store<SharedLinkRowData, (int, int, int)>(data, nameof(SharedLinkRow)).SeedRange(Links().Select(row => new SharedLinkRowData(row.Scope, row.OwnerId, row.TargetId, row.Note, row.Version, row.Hidden)));
        }
        else { Store<SharedLinkOwner, (int, int)>(data, nameof(SharedLinkOwner)).SeedRange(Owners()); Store<SharedLinkTarget, (int, int)>(data, nameof(SharedLinkTarget)).SeedRange(Targets()); Store<SharedLinkRow, (int, int, int)>(data, nameof(SharedLinkRow)).SeedRange(Links()); }
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.SharedLinkOwnerRepositoryTestDouble")!, fixture)!, data);
    }
    internal static SharedLinkOwner[] Owners() => [new() { SpaceId = 1, Id = 1, Name = "first" }, new() { SpaceId = 2, Id = 1, Name = "other" }];
    internal static SharedLinkTarget[] Targets() => [new() { PartitionId = 1, Id = 1, Name = "A" }, new() { PartitionId = 1, Id = 2, Name = "B" }, new() { PartitionId = 1, Id = 3, Name = "C" }, new() { PartitionId = 1, Id = 4, Name = "Hidden" }, new() { PartitionId = 2, Id = 1, Name = "other" }];
    internal static SharedLinkRow[] Links() => [new() { Scope = 1, OwnerId = 1, TargetId = 1, Note = "A" }, new() { Scope = 1, OwnerId = 1, TargetId = 2, Note = "B" }, new() { Scope = 1, OwnerId = 1, TargetId = 4, Note = "hidden", Hidden = true }, new() { Scope = 2, OwnerId = 1, TargetId = 1, Note = "other" }];
    internal static InMemoryStore<T, TKey> Store<T, TKey>(InMemoryDataContext data, string entity) where T : class where TKey : notnull => ManyReadTestHarness.Store<T, TKey>(data, entity);
    internal static SharedLinkRowData[] Rows(InMemoryDataContext data, bool mapped) => (mapped ? Store<SharedLinkRowData, (int, int, int)>(data, nameof(SharedLinkRow)).Snapshot() :
        Store<SharedLinkRow, (int, int, int)>(data, nameof(SharedLinkRow)).Snapshot().Select(row => new SharedLinkRowData(row.Scope, row.OwnerId, row.TargetId, row.Note, row.Version, row.Hidden)))
        .OrderBy(row => row.Scope).ThenBy(row => row.OwnerId).ThenBy(row => row.TargetId).ToArray();
}
