using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class OverlappingReferenceTestHarness
{
    internal const string Namespace = "Generated.OverlappingReferences";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    internal static OverlappingReferenceContext Context() => new(new DbContextOptionsBuilder<OverlappingReferenceContext>().UseSqlite("Data Source=:memory:").Options);
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
        var owner = builder.Entity<OverlappingReferenceOwner>(); var target = builder.Entity<OverlappingReferenceTarget>();
        var actor = builder.Entity<OverlappingReferenceActor>(); var source = builder.Entity<OverlappingReferenceSource>(); var link = builder.Entity<OverlappingReferenceLink>();
        target.WriteEnabled = actor.WriteEnabled = source.WriteEnabled = false;
        if (mapped)
        {
            owner.HasInMemoryStore<OverlappingReferenceOwnerData, (int, int, int)>(row => new ValueTuple<int, int, int>(row.PartitionId, row.ZoneId, row.Id), row => new(row.PartitionId, row.ZoneId, row.Id, row.Name, row.Version))
                .MapInMemoryEntity<OverlappingReferenceOwnerData>(row => new OverlappingReferenceOwner { PartitionId = row.PartitionId, ZoneId = row.ZoneId, Id = row.Id, Name = row.Name, Version = row.Version })
                .MapInMemoryWrites<OverlappingReferenceOwnerData>((row, saved) => new(row.PartitionId, row.ZoneId, row.Id, row.Name, row.Version));
            target.HasInMemoryStore<OverlappingReferenceTargetData, (int, int)>(row => new ValueTuple<int, int>(row.PartitionId, row.Id), row => new(row.PartitionId, row.Id, row.Name))
                .MapInMemoryEntity<OverlappingReferenceTargetData>(row => new OverlappingReferenceTarget { PartitionId = row.PartitionId, Id = row.Id, Name = row.Name });
            actor.HasInMemoryStore<OverlappingReferenceActorData, int>(row => row.Id, row => new(row.Id, row.PartitionId, row.Code, row.Name))
                .MapInMemoryEntity<OverlappingReferenceActorData>(row => new OverlappingReferenceActor { Id = row.Id, PartitionId = row.PartitionId, Code = row.Code, Name = row.Name });
            source.HasInMemoryStore<OverlappingReferenceSourceData, (int, int)>(row => new ValueTuple<int, int>(row.ZoneId, row.Id), row => new(row.ZoneId, row.Id, row.Name))
                .MapInMemoryEntity<OverlappingReferenceSourceData>(row => new OverlappingReferenceSource { ZoneId = row.ZoneId, Id = row.Id, Name = row.Name });
            link.HasInMemoryStore<OverlappingReferenceLinkData, (int, int, int, int)>(row => new ValueTuple<int, int, int, int>(row.Scope, row.Zone, row.OwnerId, row.TargetId), row => new(row.Scope, row.Zone, row.OwnerId, row.TargetId, row.ActorCode, row.ReviewerCode, row.SourceId, row.Note, row.Version, row.Hidden))
                .MapInMemoryEntity<OverlappingReferenceLinkData>(row => new OverlappingReferenceLink { Scope = row.Scope, Zone = row.Zone, OwnerId = row.OwnerId, TargetId = row.TargetId, ActorCode = row.ActorCode, ReviewerCode = row.ReviewerCode, SourceId = row.SourceId, Note = row.Note, Version = row.Version, Hidden = row.Hidden })
                .MapInMemoryWrites<OverlappingReferenceLinkData>((row, saved) => new(row.Scope, row.Zone, row.OwnerId, row.TargetId, row.ActorCode, row.ReviewerCode, row.SourceId, row.Note, row.Version, row.Hidden));
        }
        else
        {
            owner.HasInMemoryStore(row => new ValueTuple<int, int, int>(row.PartitionId, row.ZoneId, row.Id), row => new OverlappingReferenceOwner { PartitionId = row.PartitionId, ZoneId = row.ZoneId, Id = row.Id, Name = row.Name, Version = row.Version });
            target.HasInMemoryStore(row => new ValueTuple<int, int>(row.PartitionId, row.Id), row => new OverlappingReferenceTarget { PartitionId = row.PartitionId, Id = row.Id, Name = row.Name });
            actor.HasInMemoryStore(row => row.Id, row => new OverlappingReferenceActor { Id = row.Id, PartitionId = row.PartitionId, Code = row.Code, Name = row.Name });
            source.HasInMemoryStore(row => new ValueTuple<int, int>(row.ZoneId, row.Id), row => new OverlappingReferenceSource { ZoneId = row.ZoneId, Id = row.Id, Name = row.Name });
            link.HasInMemoryStore(row => new ValueTuple<int, int, int, int>(row.Scope, row.Zone, row.OwnerId, row.TargetId), row => new OverlappingReferenceLink { Scope = row.Scope, Zone = row.Zone, OwnerId = row.OwnerId, TargetId = row.TargetId, ActorCode = row.ActorCode, ReviewerCode = row.ReviewerCode, SourceId = row.SourceId, Note = row.Note, Version = row.Version, Hidden = row.Hidden });
        }
        owner.HasInMemoryNavigation(row => row.Links);
        link.HasInMemoryNavigation(row => row.Actor).HasInMemoryNavigation(row => row.Reviewer).HasInMemoryNavigation(row => row.Source);
        link.HasVisibilityPolicy("Visible", row => !row.Hidden);
        owner.HasQuery("Find").Where<OverlappingReferenceRequest>((row, request) => row.PartitionId == request.PartitionId && row.ZoneId == request.ZoneId && row.Id == request.OwnerId)
            .Select(row => new OverlappingReferenceView(row.PartitionId, row.ZoneId, row.Id, row.Version, row.Links.Where(child => !child.Hidden).OrderBy(child => child.TargetId)
                .Select(child => (child.Actor == null ? "" : child.Actor.Name) + ":" + (child.Reviewer == null ? "" : child.Reviewer.Name) + ":" + (child.Source == null ? "" : child.Source.Name)).ToList())).SingleOrDefault();
        foreach (var name in new[] { "Replace", "Merge", "Reject", "Stage", "Optional", "BadMap" })
        {
            var command = owner.HasCommand(name).Where<OverlappingReferenceRequest>((row, request) => row.PartitionId == request.PartitionId && row.ZoneId == request.ZoneId && row.Id == request.OwnerId)
                .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                .ExpectVersion<int, OverlappingReferenceRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
                .ReconcileLinks<OverlappingReferenceLink, OverlappingReferenceItem, (int, int), OverlappingReferenceRequest>(row => row.Links, request => request.Items!,
                    row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId), links =>
                    {
                        links.AllowSharedReferenceColumns(shared).TargetReference(row => row.Target)
                            .AdditionalReference(row => row.Actor).AdditionalReference(row => row.Reviewer).AdditionalReference(row => row.Source).MaximumItems(4);
                        if (name == "BadMap") links.Add(item => new OverlappingReferenceLink { Scope = item.Scope, TargetId = item.TargetId, Zone = item.Zone + 1, ActorCode = item.ActorCode, ReviewerCode = item.ReviewerCode, SourceId = item.SourceId, Note = item.Note, Version = 1, Hidden = false });
                        else links.Add(item => new OverlappingReferenceLink { Scope = item.Scope, TargetId = item.TargetId, Zone = item.Zone, ActorCode = item.ActorCode, ReviewerCode = item.ReviewerCode, SourceId = item.SourceId, Note = item.Note, Version = 1, Hidden = false });
                        links.Update(setters => setters.SetProperty(row => row.Note, (row, item) => item.Note).SetProperty(row => row.Version, row => row.Version + 1))
                            .AllowWrite(row => row.Version).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                            .MissingItems(name == "Merge" || name == "BadMap" ? RepositoryCollectionRemovalBehavior.Keep : name == "Reject" ? RepositoryCollectionRemovalBehavior.Reject : RepositoryCollectionRemovalBehavior.Delete);
                        if (name == "Optional") links.When<OverlappingReferenceRequest>(request => request.Apply);
                    }).SimulateCollectionWrites(simulate);
            if (name == "Stage" || name == "BadMap" || name == "Optional") command.Stage();
            else command.Save().ReturnsSaved(row => new OverlappingReferenceView(row.PartitionId, row.ZoneId, row.Id, row.Version, row.Links.Where(child => !child.Hidden).OrderBy(child => child.TargetId)
                .Select(child => (child.Actor == null ? "" : child.Actor.Name) + ":" + (child.Reviewer == null ? "" : child.Reviewer.Name) + ":" + (child.Source == null ? "" : child.Source.Name)).ToList()));
        }
        owner.HasCommand("StageName").Where<int>((row, partition) => row.PartitionId == partition).Patch(setters => setters.SetProperty(row => row.Name, "pending")).Stage();
        return builder;
    }

    internal static (object Repository, InMemoryWriteDataContext Data) Repositories(bool mapped = true)
    {
        var assembly = mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.OverlappingReferenceContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped)
        {
            Store<OverlappingReferenceOwnerData, (int, int, int)>(data, nameof(OverlappingReferenceOwner)).SeedRange(Owners().Select(row => new OverlappingReferenceOwnerData(row.PartitionId, row.ZoneId, row.Id, row.Name, row.Version)));
            Store<OverlappingReferenceTargetData, (int, int)>(data, nameof(OverlappingReferenceTarget)).SeedRange(Targets().Select(row => new OverlappingReferenceTargetData(row.PartitionId, row.Id, row.Name)));
            Store<OverlappingReferenceActorData, int>(data, nameof(OverlappingReferenceActor)).SeedRange(Actors().Select(row => new OverlappingReferenceActorData(row.Id, row.PartitionId, row.Code, row.Name)));
            Store<OverlappingReferenceSourceData, (int, int)>(data, nameof(OverlappingReferenceSource)).SeedRange(Sources().Select(row => new OverlappingReferenceSourceData(row.ZoneId, row.Id, row.Name)));
            Store<OverlappingReferenceLinkData, (int, int, int, int)>(data, nameof(OverlappingReferenceLink)).SeedRange(Links().Select(Row));
        }
        else
        {
            Store<OverlappingReferenceOwner, (int, int, int)>(data, nameof(OverlappingReferenceOwner)).SeedRange(Owners());
            Store<OverlappingReferenceTarget, (int, int)>(data, nameof(OverlappingReferenceTarget)).SeedRange(Targets());
            Store<OverlappingReferenceActor, int>(data, nameof(OverlappingReferenceActor)).SeedRange(Actors());
            Store<OverlappingReferenceSource, (int, int)>(data, nameof(OverlappingReferenceSource)).SeedRange(Sources());
            Store<OverlappingReferenceLink, (int, int, int, int)>(data, nameof(OverlappingReferenceLink)).SeedRange(Links());
        }
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.OverlappingReferenceOwnerRepositoryTestDouble")!, fixture)!, data);
    }
    internal static OverlappingReferenceOwner[] Owners() => [new() { PartitionId = 1, ZoneId = 10, Id = 1, Name = "first" }, new() { PartitionId = 2, ZoneId = 20, Id = 1, Name = "other" }];
    internal static OverlappingReferenceTarget[] Targets() => [new() { PartitionId = 1, Id = 1, Name = "A" }, new() { PartitionId = 1, Id = 2, Name = "B" }, new() { PartitionId = 1, Id = 3, Name = "C" }, new() { PartitionId = 1, Id = 4, Name = "Hidden" }, new() { PartitionId = 2, Id = 1, Name = "other" }];
    internal static OverlappingReferenceActor[] Actors() => [new() { Id = 1, PartitionId = 1, Code = "a", Name = "Actor A" }, new() { Id = 2, PartitionId = 1, Code = "b", Name = "Actor B" }, new() { Id = 3, PartitionId = 2, Code = "a", Name = "Other actor" }];
    internal static OverlappingReferenceSource[] Sources() => [new() { ZoneId = 10, Id = 1, Name = "Source A" }, new() { ZoneId = 10, Id = 2, Name = "Source B" }, new() { ZoneId = 20, Id = 1, Name = "Other source" }];
    internal static OverlappingReferenceLink[] Links() => [new() { Scope = 1, Zone = 10, OwnerId = 1, TargetId = 1, ActorCode = "a", ReviewerCode = "b", SourceId = 1, Note = "A" },
        new() { Scope = 1, Zone = 10, OwnerId = 1, TargetId = 2, ActorCode = "b", ReviewerCode = "a", SourceId = 2, Note = "B" },
        new() { Scope = 1, Zone = 10, OwnerId = 1, TargetId = 4, ActorCode = "a", ReviewerCode = "b", SourceId = 1, Note = "hidden", Hidden = true },
        new() { Scope = 2, Zone = 20, OwnerId = 1, TargetId = 1, ActorCode = "a", ReviewerCode = "a", SourceId = 1, Note = "other" }];
    internal static OverlappingReferenceLinkData Row(OverlappingReferenceLink row) => new(row.Scope, row.Zone, row.OwnerId, row.TargetId, row.ActorCode, row.ReviewerCode, row.SourceId, row.Note, row.Version, row.Hidden);
    internal static OverlappingReferenceLinkData[] Rows(InMemoryDataContext data, bool mapped) => (mapped
        ? Store<OverlappingReferenceLinkData, (int, int, int, int)>(data, nameof(OverlappingReferenceLink)).Snapshot()
        : Store<OverlappingReferenceLink, (int, int, int, int)>(data, nameof(OverlappingReferenceLink)).Snapshot().Select(Row)).OrderBy(row => row.Scope).ThenBy(row => row.TargetId).ToArray();
    internal static InMemoryStore<T, TKey> Store<T, TKey>(InMemoryDataContext data, string name) where T : class where TKey : notnull => ManyReadTestHarness.Store<T, TKey>(data, name);
}
