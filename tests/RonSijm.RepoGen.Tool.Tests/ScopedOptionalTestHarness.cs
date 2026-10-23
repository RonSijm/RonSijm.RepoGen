using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class ScopedOptionalTestHarness
{
    internal const string Namespace = "Generated.ScopedOptional";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    internal static DbContextOptions<ScopedOptionalContext> Options() => new DbContextOptionsBuilder<ScopedOptionalContext>().UseSqlite("Data Source=:memory:").Options;
    internal static ScopedOptionalContext Context() => new(Options());
    private static Assembly Compile(bool mapped)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool shared = true, bool simulate = true, bool strict = true, bool backend = true,
        Action<RepositoryCollectionReconciliationBuilder<ScopedOptionalRow, ScopedOptionalItem>>? change = null)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".TestDoubles").UseInMemoryTestStores().UseInMemoryTestQueries()
            .UseInMemoryTestCommands(backend).RequirePortableTestQueries().RequirePortableTestCommands(strict && backend));
        var owner = builder.Entity<ScopedOptionalOwner>(); var target = builder.Entity<ScopedOptionalTarget>();
        var reviewer = builder.Entity<ScopedOptionalReviewer>(); var origin = builder.Entity<ScopedOptionalOrigin>(); var link = builder.Entity<ScopedOptionalRow>();
        target.WriteEnabled = reviewer.WriteEnabled = origin.WriteEnabled = false;
        if (mapped)
        {
            owner.HasInMemoryStore<ScopedOptionalOwnerData, (int, int, int)>(row => new ValueTuple<int, int, int>(row.PartitionId, row.ZoneId, row.Id), row => new(row.PartitionId, row.ZoneId, row.Id, row.Name, row.Version))
                .MapInMemoryEntity<ScopedOptionalOwnerData>(row => new ScopedOptionalOwner { PartitionId = row.PartitionId, ZoneId = row.ZoneId, Id = row.Id, Name = row.Name, Version = row.Version })
                .MapInMemoryWrites<ScopedOptionalOwnerData>((row, saved) => new(row.PartitionId, row.ZoneId, row.Id, row.Name, row.Version));
            target.HasInMemoryStore<ScopedOptionalTargetData, (int, int)>(row => new ValueTuple<int, int>(row.PartitionId, row.Id), row => new(row.PartitionId, row.Id))
                .MapInMemoryEntity<ScopedOptionalTargetData>(row => new ScopedOptionalTarget { PartitionId = row.PartitionId, Id = row.Id });
            reviewer.HasInMemoryStore<ScopedOptionalReviewerData, int>(row => row.Id, row => new(row.Id, row.PartitionId, row.Region, row.Code, row.Name))
                .MapInMemoryEntity<ScopedOptionalReviewerData>(row => new ScopedOptionalReviewer { Id = row.Id, PartitionId = row.PartitionId, Region = row.Region, Code = row.Code, Name = row.Name });
            origin.HasInMemoryStore<ScopedOptionalOriginData, (int, int)>(row => new ValueTuple<int, int>(row.ZoneId, row.Id), row => new(row.ZoneId, row.Id, row.Name))
                .MapInMemoryEntity<ScopedOptionalOriginData>(row => new ScopedOptionalOrigin { ZoneId = row.ZoneId, Id = row.Id, Name = row.Name });
            link.HasInMemoryStore<ScopedOptionalData, (int, int, int, int)>(row => new ValueTuple<int, int, int, int>(row.Scope, row.Zone, row.OwnerId, row.TargetId), row => new(row.Scope, row.Zone, row.OwnerId, row.TargetId, row.ActorRegion, row.ActorCode, row.ReviewerRegion, row.ReviewerCode, row.OriginId, row.Note, row.Version, row.Hidden))
                .MapInMemoryEntity<ScopedOptionalData>(row => new ScopedOptionalRow { Scope = row.Scope, Zone = row.Zone, OwnerId = row.OwnerId, TargetId = row.TargetId, ActorRegion = row.ActorRegion, ActorCode = row.ActorCode, ReviewerRegion = row.ReviewerRegion, ReviewerCode = row.ReviewerCode, OriginId = row.OriginId, Note = row.Note, Version = row.Version, Hidden = row.Hidden })
                .MapInMemoryWrites<ScopedOptionalData>((row, saved) => new(row.Scope, row.Zone, row.OwnerId, row.TargetId, row.ActorRegion, row.ActorCode, row.ReviewerRegion, row.ReviewerCode, row.OriginId, row.Note, row.Version, row.Hidden));
        }
        else
        {
            owner.HasInMemoryStore(row => new ValueTuple<int, int, int>(row.PartitionId, row.ZoneId, row.Id), row => new ScopedOptionalOwner { PartitionId = row.PartitionId, ZoneId = row.ZoneId, Id = row.Id, Name = row.Name, Version = row.Version });
            target.HasInMemoryStore(row => new ValueTuple<int, int>(row.PartitionId, row.Id), row => new ScopedOptionalTarget { PartitionId = row.PartitionId, Id = row.Id });
            reviewer.HasInMemoryStore(row => row.Id, row => new ScopedOptionalReviewer { Id = row.Id, PartitionId = row.PartitionId, Region = row.Region, Code = row.Code, Name = row.Name });
            origin.HasInMemoryStore(row => new ValueTuple<int, int>(row.ZoneId, row.Id), row => new ScopedOptionalOrigin { ZoneId = row.ZoneId, Id = row.Id, Name = row.Name });
            link.HasInMemoryStore(row => new ValueTuple<int, int, int, int>(row.Scope, row.Zone, row.OwnerId, row.TargetId), row => new ScopedOptionalRow { Scope = row.Scope, Zone = row.Zone, OwnerId = row.OwnerId, TargetId = row.TargetId, ActorRegion = row.ActorRegion, ActorCode = row.ActorCode, ReviewerRegion = row.ReviewerRegion, ReviewerCode = row.ReviewerCode, OriginId = row.OriginId, Note = row.Note, Version = row.Version, Hidden = row.Hidden });
        }
        owner.HasInMemoryNavigation(row => row.Links);
        link.HasInMemoryNavigation(row => row.Reviewer).HasInMemoryNavigation(row => row.Origin).HasVisibilityPolicy("Visible", row => !row.Hidden);
        owner.HasQuery("Find").Where<ScopedOptionalRequest>((row, request) => row.PartitionId == request.PartitionId && row.ZoneId == request.ZoneId && row.Id == request.OwnerId)
            .Select(row => new ScopedOptionalView(row.PartitionId, row.ZoneId, row.Id, row.Version, row.Links.Where(child => !child.Hidden).OrderBy(child => child.TargetId)
                .Select(child => new ScopedOptionalData(child.Scope, child.Zone, child.OwnerId, child.TargetId, child.ActorRegion, child.ActorCode, child.ReviewerRegion, child.ReviewerCode, child.OriginId, child.Note, child.Version, child.Hidden)).ToList())).SingleOrDefault();
        owner.HasQuery("Names").Where<ScopedOptionalRequest>((row, request) => row.PartitionId == request.PartitionId && row.ZoneId == request.ZoneId && row.Id == request.OwnerId)
            .Select(row => row.Links.Where(child => !child.Hidden).OrderBy(child => child.TargetId)
                .Select(child => (child.Reviewer == null ? "none" : child.Reviewer.Name) + ":" + (child.Origin == null ? "none" : child.Origin.Name)).ToList()).SingleOrDefault();
        foreach (var operation in new[] { "Merge", "Replace", "Reject", "Stage", "Optional", "BadMap" })
        {
            var command = owner.HasCommand(operation).Where<ScopedOptionalRequest>((row, request) => row.PartitionId == request.PartitionId && row.ZoneId == request.ZoneId && row.Id == request.OwnerId)
                .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                .ExpectVersion<int, ScopedOptionalRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
                .ReconcileLinks<ScopedOptionalRow, ScopedOptionalItem, (int, int), ScopedOptionalRequest>(row => row.Links, request => request.Items!,
                    row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId), links =>
                    {
                        links.AllowSharedReferenceColumns(shared).TargetReference(row => row.Target).AdditionalReference(row => row.Actor)
                            .OptionalAdditionalReference(row => row.Reviewer).OptionalAdditionalReference(row => row.Origin).MaximumItems(5);
                        if (operation == "BadMap") links.Add(item => new ScopedOptionalRow { Scope = item.Scope, Zone = item.Zone + 1, TargetId = item.TargetId, ActorRegion = "eu", ActorCode = "a", ReviewerRegion = item.ReviewerRegion, ReviewerCode = item.ReviewerCode, OriginId = item.OriginId, Note = item.Note, Version = 1, Hidden = false });
                        else links.Add(item => new ScopedOptionalRow { Scope = item.Scope, Zone = item.Zone, TargetId = item.TargetId, ActorRegion = "eu", ActorCode = "a", ReviewerRegion = item.ReviewerRegion, ReviewerCode = item.ReviewerCode, OriginId = item.OriginId, Note = item.Note, Version = 1, Hidden = false });
                        links.Update(setters => setters.SetProperty(row => row.Note, (row, item) => item.Note).SetProperty(row => row.Version, row => row.Version + 1))
                            .AllowWrite(row => row.Version).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                            .MissingItems(operation is "Merge" or "BadMap" ? RepositoryCollectionRemovalBehavior.Keep : operation == "Reject" ? RepositoryCollectionRemovalBehavior.Reject : RepositoryCollectionRemovalBehavior.Delete);
                        if (operation == "Optional") links.When<ScopedOptionalRequest>(request => request.Apply);
                        change?.Invoke(links);
                    }).SimulateCollectionWrites(simulate);
            if (operation is "Stage" or "Optional" or "BadMap") command.Stage();
            else command.Save().ReturnsSaved(row => new ScopedOptionalView(row.PartitionId, row.ZoneId, row.Id, row.Version, row.Links.Where(child => !child.Hidden).OrderBy(child => child.TargetId)
                .Select(child => new ScopedOptionalData(child.Scope, child.Zone, child.OwnerId, child.TargetId, child.ActorRegion, child.ActorCode, child.ReviewerRegion, child.ReviewerCode, child.OriginId, child.Note, child.Version, child.Hidden)).ToList()));
        }
        owner.HasCommand("StageName").Where<int>((row, partition) => row.PartitionId == partition).Patch(setters => setters.SetProperty(row => row.Name, "pending")).Stage();
        return builder;
    }

    internal static (object Repository, InMemoryWriteDataContext Data) Repositories(bool mapped = true, bool partialSeed = false)
    {
        var assembly = mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.ScopedOptionalContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var links = Links();
        if (partialSeed) links.Single(row => row.Scope == 1 && row.TargetId == 2).ReviewerRegion = "eu";
        if (mapped)
        {
            Store<ScopedOptionalOwnerData, (int, int, int)>(data, nameof(ScopedOptionalOwner)).SeedRange(Owners().Select(row => new ScopedOptionalOwnerData(row.PartitionId, row.ZoneId, row.Id, row.Name, row.Version)));
            Store<ScopedOptionalTargetData, (int, int)>(data, nameof(ScopedOptionalTarget)).SeedRange(Targets().Select(row => new ScopedOptionalTargetData(row.PartitionId, row.Id)));
            Store<ScopedOptionalReviewerData, int>(data, nameof(ScopedOptionalReviewer)).SeedRange(Reviewers().Select(row => new ScopedOptionalReviewerData(row.Id, row.PartitionId, row.Region, row.Code, row.Name)));
            Store<ScopedOptionalOriginData, (int, int)>(data, nameof(ScopedOptionalOrigin)).SeedRange(Origins().Select(row => new ScopedOptionalOriginData(row.ZoneId, row.Id, row.Name)));
            Store<ScopedOptionalData, (int, int, int, int)>(data, nameof(ScopedOptionalRow)).SeedRange(links.Select(Row));
        }
        else
        {
            Store<ScopedOptionalOwner, (int, int, int)>(data, nameof(ScopedOptionalOwner)).SeedRange(Owners());
            Store<ScopedOptionalTarget, (int, int)>(data, nameof(ScopedOptionalTarget)).SeedRange(Targets());
            Store<ScopedOptionalReviewer, int>(data, nameof(ScopedOptionalReviewer)).SeedRange(Reviewers());
            Store<ScopedOptionalOrigin, (int, int)>(data, nameof(ScopedOptionalOrigin)).SeedRange(Origins());
            Store<ScopedOptionalRow, (int, int, int, int)>(data, nameof(ScopedOptionalRow)).SeedRange(links);
        }
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.ScopedOptionalOwnerRepositoryTestDouble")!, fixture)!, data);
    }
    internal static ScopedOptionalOwner[] Owners() => [new() { PartitionId = 1, ZoneId = 10, Id = 1 }, new() { PartitionId = 2, ZoneId = 20, Id = 1, Name = "other" }];
    internal static ScopedOptionalTarget[] Targets() => Enumerable.Range(1, 6).Select(id => new ScopedOptionalTarget { PartitionId = 1, Id = id }).Append(new() { PartitionId = 2, Id = 1 }).ToArray();
    internal static ScopedOptionalReviewer[] Reviewers() => [new() { Id = 1, PartitionId = 1, Region = "eu", Code = "a", Name = "Reviewer A" }, new() { Id = 2, PartitionId = 1, Region = "us", Code = "b", Name = "Reviewer B" }, new() { Id = 3, PartitionId = 2, Region = "eu", Code = "a", Name = "Other reviewer" }];
    internal static ScopedOptionalOrigin[] Origins() => [new() { ZoneId = 10, Id = 1, Name = "Origin A" }, new() { ZoneId = 10, Id = 2, Name = "Origin B" }, new() { ZoneId = 20, Id = 1, Name = "Other origin" }];
    internal static ScopedOptionalRow[] Links() => [new() { Scope = 1, Zone = 10, OwnerId = 1, TargetId = 1, ActorRegion = "eu", ActorCode = "a", ReviewerRegion = "eu", ReviewerCode = "a", OriginId = 1, Note = "present" },
        new() { Scope = 1, Zone = 10, OwnerId = 1, TargetId = 2, ActorRegion = "eu", ActorCode = "a", Note = "absent" },
        new() { Scope = 1, Zone = 10, OwnerId = 1, TargetId = 6, ActorRegion = "eu", ActorCode = "a", ReviewerRegion = "us", ReviewerCode = "b", OriginId = 2, Note = "hidden", Hidden = true },
        new() { Scope = 2, Zone = 20, OwnerId = 1, TargetId = 1, ActorRegion = "eu", ActorCode = "a", ReviewerRegion = "eu", ReviewerCode = "a", OriginId = 1, Note = "other" }];
    internal static ScopedOptionalData Row(ScopedOptionalRow row) => new(row.Scope, row.Zone, row.OwnerId, row.TargetId, row.ActorRegion, row.ActorCode, row.ReviewerRegion, row.ReviewerCode, row.OriginId, row.Note, row.Version, row.Hidden);
    internal static ScopedOptionalData[] Rows(InMemoryDataContext data, bool mapped) => (mapped
        ? Store<ScopedOptionalData, (int, int, int, int)>(data, nameof(ScopedOptionalRow)).Snapshot()
        : Store<ScopedOptionalRow, (int, int, int, int)>(data, nameof(ScopedOptionalRow)).Snapshot().Select(Row)).OrderBy(row => row.Scope).ThenBy(row => row.TargetId).ToArray();
    internal static InMemoryStore<T, TKey> Store<T, TKey>(InMemoryDataContext data, string name) where T : class where TKey : notnull => ManyReadTestHarness.Store<T, TKey>(data, name);
}
