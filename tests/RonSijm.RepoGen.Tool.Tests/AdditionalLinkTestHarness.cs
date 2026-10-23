using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class AdditionalLinkTestHarness
{
    internal const string Namespace = "Generated.AdditionalLinks";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    internal static AdditionalLinkContext Context() => new(new DbContextOptionsBuilder<AdditionalLinkContext>().UseSqlite("Data Source=:memory:").Options);
    private static Assembly Compile(bool mapped)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }
    internal static RepositoryModelBuilder Builder(bool mapped = true, bool roles = true, bool simulate = true, bool strict = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".TestDoubles").UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands(strict));
        var owner = builder.Entity<AdditionalLinkOwner>(); var link = builder.Entity<AdditionalLinkRow>();
        var target = builder.Entity<AdditionalLinkTarget>(); var actor = builder.Entity<AdditionalLinkActor>(); var source = builder.Entity<AdditionalLinkSource>();
        target.WriteEnabled = false; actor.WriteEnabled = false; source.WriteEnabled = false;
        if (mapped)
        {
            owner.HasInMemoryStore<AdditionalLinkOwnerData, (int, int)>(row => new ValueTuple<int, int>(row.Scope, row.Id), row => new(row.Scope, row.Id, row.Name, row.Version))
                .MapInMemoryEntity<AdditionalLinkOwnerData>(row => new AdditionalLinkOwner { Scope = row.Scope, Id = row.Id, Name = row.Name, Version = row.Version })
                .MapInMemoryWrites<AdditionalLinkOwnerData>((row, saved) => new(row.Scope, row.Id, row.Name, row.Version));
            link.HasInMemoryStore<AdditionalLinkData, (int, int, int)>(row => new ValueTuple<int, int, int>(row.Scope, row.OwnerId, row.TargetId), row => new(row.Scope, row.OwnerId, row.TargetId, row.ActorRegion, row.ActorCode, row.SourceId, row.Note, row.Version, row.Hidden))
                .MapInMemoryEntity<AdditionalLinkData>(row => new AdditionalLinkRow { Scope = row.Scope, OwnerId = row.OwnerId, TargetId = row.TargetId, ActorRegion = row.ActorRegion, ActorCode = row.ActorCode, SourceId = row.SourceId, Note = row.Note, Version = row.Version, Hidden = row.Hidden })
                .MapInMemoryWrites<AdditionalLinkData>((row, saved) => new(row.Scope, row.OwnerId, row.TargetId, row.ActorRegion, row.ActorCode, row.SourceId, row.Note, row.Version, row.Hidden));
            target.HasInMemoryStore<AdditionalLinkTargetData, (int, int)>(row => new ValueTuple<int, int>(row.Scope, row.Id), row => new(row.Scope, row.Id, row.Name))
                .MapInMemoryEntity<AdditionalLinkTargetData>(row => new AdditionalLinkTarget { Scope = row.Scope, Id = row.Id, Name = row.Name });
            actor.HasInMemoryStore<AdditionalLinkActorData, int>(row => row.Id, row => new(row.Id, row.Region, row.Code, row.Name))
                .MapInMemoryEntity<AdditionalLinkActorData>(row => new AdditionalLinkActor { Id = row.Id, Region = row.Region, Code = row.Code, Name = row.Name });
            source.HasInMemoryStore<AdditionalLinkSourceData, int>(row => row.Id, row => new(row.Id, row.Name))
                .MapInMemoryEntity<AdditionalLinkSourceData>(row => new AdditionalLinkSource { Id = row.Id, Name = row.Name });
        }
        else
        {
            owner.HasInMemoryStore(row => new ValueTuple<int, int>(row.Scope, row.Id), row => new AdditionalLinkOwner { Scope = row.Scope, Id = row.Id, Name = row.Name, Version = row.Version });
            link.HasInMemoryStore(row => new ValueTuple<int, int, int>(row.Scope, row.OwnerId, row.TargetId), row => new AdditionalLinkRow { Scope = row.Scope, OwnerId = row.OwnerId, TargetId = row.TargetId, ActorRegion = row.ActorRegion, ActorCode = row.ActorCode, SourceId = row.SourceId, Note = row.Note, Version = row.Version, Hidden = row.Hidden });
            target.HasInMemoryStore(row => new ValueTuple<int, int>(row.Scope, row.Id), row => new AdditionalLinkTarget { Scope = row.Scope, Id = row.Id, Name = row.Name });
            actor.HasInMemoryStore(row => row.Id, row => new AdditionalLinkActor { Id = row.Id, Region = row.Region, Code = row.Code, Name = row.Name });
            source.HasInMemoryStore(row => row.Id, row => new AdditionalLinkSource { Id = row.Id, Name = row.Name });
        }
        owner.HasInMemoryNavigation(row => row.Links);
        link.HasVisibilityPolicy("Visible", row => !row.Hidden); link.HasInMemoryNavigation(row => row.Actor).HasInMemoryNavigation(row => row.Source);
        owner.HasQuery("Find").Where<AdditionalLinkRequest>((row, request) => row.Scope == request.Scope && row.Id == request.OwnerId)
            .Select(row => new AdditionalLinkView(row.Scope, row.Id, row.Version, row.Links.Where(child => !child.Hidden).OrderBy(child => child.TargetId)
                .Select(child => (child.Actor == null ? "" : child.Actor.Name) + ":" + (child.Source == null ? "" : child.Source.Name)).ToList())).SingleOrDefault();
        foreach (var name in new[] { "Merge", "Replace", "Stage" })
        {
            var command = owner.HasCommand(name).Where<AdditionalLinkRequest>((row, request) => row.Scope == request.Scope && row.Id == request.OwnerId)
                .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                .ExpectVersion<int, AdditionalLinkRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
                .ReconcileLinks<AdditionalLinkRow, AdditionalLinkItem, (int, int), AdditionalLinkRequest>(row => row.Links, request => request.Items!,
                    row => new ValueTuple<int, int>(row.Scope, row.TargetId), item => new ValueTuple<int, int>(item.Scope, item.TargetId), links =>
                    {
                        links.AllowSharedReferenceColumns().MaximumItems(4);
                        if (roles) links.TargetReference(row => row.Target).AdditionalReference(row => row.Actor).AdditionalReference(row => row.Source);
                        links.Add(item => new AdditionalLinkRow { Scope = item.Scope, TargetId = item.TargetId, ActorRegion = item.ActorRegion, ActorCode = item.ActorCode, SourceId = item.SourceId, Note = item.Note, Version = 1, Hidden = false })
                            .Update(setters => setters.SetProperty(row => row.Note, (row, item) => item.Note).SetProperty(row => row.Version, row => row.Version + 1))
                            .AllowWrite(row => row.Version).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                            .MissingItems(name == "Merge" ? RepositoryCollectionRemovalBehavior.Keep : RepositoryCollectionRemovalBehavior.Delete);
                    }).SimulateCollectionWrites(simulate);
            if (name == "Stage") command.Stage();
            else command.Save().ReturnsSaved(row => new AdditionalLinkView(row.Scope, row.Id, row.Version, row.Links.Where(child => !child.Hidden).OrderBy(child => child.TargetId)
                .Select(child => (child.Actor == null ? "" : child.Actor.Name) + ":" + (child.Source == null ? "" : child.Source.Name)).ToList()));
        }
        owner.HasCommand("StageName").Where<int>((row, scope) => row.Scope == scope).Patch(setters => setters.SetProperty(row => row.Name, "pending")).Stage();
        return builder;
    }
    internal static (object Owner, InMemoryWriteDataContext Data) Repositories(bool mapped = true)
    {
        var assembly = mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.AdditionalLinkContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped)
        {
            Store<AdditionalLinkOwnerData, (int, int)>(data, nameof(AdditionalLinkOwner)).SeedRange(Owners().Select(row => new AdditionalLinkOwnerData(row.Scope, row.Id, row.Name, row.Version)));
            Store<AdditionalLinkTargetData, (int, int)>(data, nameof(AdditionalLinkTarget)).SeedRange(Targets().Select(row => new AdditionalLinkTargetData(row.Scope, row.Id, row.Name)));
            Store<AdditionalLinkActorData, int>(data, nameof(AdditionalLinkActor)).SeedRange(Actors().Select(row => new AdditionalLinkActorData(row.Id, row.Region, row.Code, row.Name)));
            Store<AdditionalLinkSourceData, int>(data, nameof(AdditionalLinkSource)).SeedRange(Sources().Select(row => new AdditionalLinkSourceData(row.Id, row.Name)));
            Store<AdditionalLinkData, (int, int, int)>(data, nameof(AdditionalLinkRow)).SeedRange(Links().Select(row => new AdditionalLinkData(row.Scope, row.OwnerId, row.TargetId, row.ActorRegion, row.ActorCode, row.SourceId, row.Note, row.Version, row.Hidden)));
        }
        else { Store<AdditionalLinkOwner, (int, int)>(data, nameof(AdditionalLinkOwner)).SeedRange(Owners()); Store<AdditionalLinkTarget, (int, int)>(data, nameof(AdditionalLinkTarget)).SeedRange(Targets()); Store<AdditionalLinkActor, int>(data, nameof(AdditionalLinkActor)).SeedRange(Actors()); Store<AdditionalLinkSource, int>(data, nameof(AdditionalLinkSource)).SeedRange(Sources()); Store<AdditionalLinkRow, (int, int, int)>(data, nameof(AdditionalLinkRow)).SeedRange(Links()); }
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.AdditionalLinkOwnerRepositoryTestDouble")!, fixture)!, data);
    }
    internal static AdditionalLinkOwner[] Owners() => [new() { Scope = 1, Id = 1, Name = "first" }, new() { Scope = 2, Id = 1, Name = "other" }];
    internal static AdditionalLinkTarget[] Targets() => [new() { Scope = 1, Id = 1, Name = "A" }, new() { Scope = 1, Id = 2, Name = "B" }, new() { Scope = 1, Id = 3, Name = "C" }, new() { Scope = 1, Id = 4, Name = "Hidden" }, new() { Scope = 2, Id = 1, Name = "other" }];
    internal static AdditionalLinkActor[] Actors() => [new() { Id = 1, Region = "eu", Code = "a", Name = "Actor A" }, new() { Id = 2, Region = "us", Code = "b", Name = "Actor B" }];
    internal static AdditionalLinkSource[] Sources() => [new() { Id = 1, Name = "Source A" }, new() { Id = 2, Name = "Source B" }];
    internal static AdditionalLinkRow[] Links() => [new() { Scope = 1, OwnerId = 1, TargetId = 1, ActorRegion = "eu", ActorCode = "a", SourceId = 1, Note = "A" },
        new() { Scope = 1, OwnerId = 1, TargetId = 2, ActorRegion = "us", ActorCode = "b", SourceId = 2, Note = "B" },
        new() { Scope = 1, OwnerId = 1, TargetId = 4, ActorRegion = "eu", ActorCode = "a", SourceId = 1, Note = "hidden", Hidden = true },
        new() { Scope = 2, OwnerId = 1, TargetId = 1, ActorRegion = "us", ActorCode = "b", SourceId = 2, Note = "other" }];
    internal static InMemoryStore<T, TKey> Store<T, TKey>(InMemoryDataContext data, string name) where T : class where TKey : notnull => ManyReadTestHarness.Store<T, TKey>(data, name);
    internal static AdditionalLinkData[] Rows(InMemoryDataContext data, bool mapped) => (mapped ? Store<AdditionalLinkData, (int, int, int)>(data, nameof(AdditionalLinkRow)).Snapshot() :
        Store<AdditionalLinkRow, (int, int, int)>(data, nameof(AdditionalLinkRow)).Snapshot().Select(row => new AdditionalLinkData(row.Scope, row.OwnerId, row.TargetId, row.ActorRegion, row.ActorCode, row.SourceId, row.Note, row.Version, row.Hidden))).OrderBy(row => row.Scope).ThenBy(row => row.TargetId).ToArray();
}
