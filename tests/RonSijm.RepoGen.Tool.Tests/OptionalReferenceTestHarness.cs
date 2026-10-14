using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class OptionalReferenceTestHarness
{
    internal const string Namespace = "Generated.OptionalReferences";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));
    internal static DbContextOptions<OptionalReferenceContext> Options() => new DbContextOptionsBuilder<OptionalReferenceContext>().UseSqlite("Data Source=:memory:").Options;
    internal static OptionalReferenceContext Context() => new(Options());
    private static Assembly Compile(bool mapped)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool simulate = true, bool strict = true,
        Action<RepositoryCollectionReconciliationBuilder<OptionalReferenceRow, OptionalReferenceItem>>? change = null)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".TestDoubles").UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands(strict));
        var owner = builder.Entity<OptionalReferenceOwner>(); var link = builder.Entity<OptionalReferenceRow>();
        var target = builder.Entity<OptionalReferenceTarget>(); var reviewer = builder.Entity<OptionalReferenceReviewer>(); var source = builder.Entity<OptionalReferenceSource>();
        target.WriteEnabled = false; reviewer.WriteEnabled = false; source.WriteEnabled = false;
        if (mapped)
        {
            owner.HasInMemoryStore<OptionalReferenceOwnerData, int>(row => row.Id, row => new(row.Id, row.Name, row.Version))
                .MapInMemoryEntity<OptionalReferenceOwnerData>(row => new OptionalReferenceOwner { Id = row.Id, Name = row.Name, Version = row.Version })
                .MapInMemoryWrites<OptionalReferenceOwnerData>((row, saved) => new(row.Id, row.Name, row.Version));
            link.HasInMemoryStore<OptionalReferenceData, (int, int)>(row => new ValueTuple<int, int>(row.OwnerId, row.TargetId), row => new(row.OwnerId, row.TargetId, row.ReviewerRegion, row.ReviewerCode, row.SourceId, row.Note, row.Version, row.Hidden))
                .MapInMemoryEntity<OptionalReferenceData>(row => new OptionalReferenceRow { OwnerId = row.OwnerId, TargetId = row.TargetId, ReviewerRegion = row.ReviewerRegion, ReviewerCode = row.ReviewerCode, SourceId = row.SourceId, Note = row.Note, Version = row.Version, Hidden = row.Hidden })
                .MapInMemoryWrites<OptionalReferenceData>((row, saved) => new(row.OwnerId, row.TargetId, row.ReviewerRegion, row.ReviewerCode, row.SourceId, row.Note, row.Version, row.Hidden));
            target.HasInMemoryStore<OptionalReferenceTargetData, int>(row => row.Id, row => new(row.Id))
                .MapInMemoryEntity<OptionalReferenceTargetData>(row => new OptionalReferenceTarget { Id = row.Id });
            reviewer.HasInMemoryStore<OptionalReferenceReviewerData, int>(row => row.Id, row => new(row.Id, row.Region, row.Code))
                .MapInMemoryEntity<OptionalReferenceReviewerData>(row => new OptionalReferenceReviewer { Id = row.Id, Region = row.Region, Code = row.Code });
            source.HasInMemoryStore<OptionalReferenceSourceData, int>(row => row.Id, row => new(row.Id))
                .MapInMemoryEntity<OptionalReferenceSourceData>(row => new OptionalReferenceSource { Id = row.Id });
        }
        else
        {
            owner.HasInMemoryStore(row => row.Id, row => new OptionalReferenceOwner { Id = row.Id, Name = row.Name, Version = row.Version });
            link.HasInMemoryStore(row => new ValueTuple<int, int>(row.OwnerId, row.TargetId), row => new OptionalReferenceRow { OwnerId = row.OwnerId, TargetId = row.TargetId, ReviewerRegion = row.ReviewerRegion, ReviewerCode = row.ReviewerCode, SourceId = row.SourceId, Note = row.Note, Version = row.Version, Hidden = row.Hidden });
            target.HasInMemoryStore(row => row.Id, row => new OptionalReferenceTarget { Id = row.Id });
            reviewer.HasInMemoryStore(row => row.Id, row => new OptionalReferenceReviewer { Id = row.Id, Region = row.Region, Code = row.Code });
            source.HasInMemoryStore(row => row.Id, row => new OptionalReferenceSource { Id = row.Id });
        }
        owner.HasInMemoryNavigation(row => row.Links); link.HasVisibilityPolicy("Visible", row => !row.Hidden);
        owner.HasQuery("Find").Where<int>((row, id) => row.Id == id).Select(row => new OptionalReferenceView(row.Id, row.Version,
            row.Links.Where(child => !child.Hidden).OrderBy(child => child.TargetId)
                .Select(child => new OptionalReferenceData(child.OwnerId, child.TargetId, child.ReviewerRegion, child.ReviewerCode, child.SourceId, child.Note, child.Version, child.Hidden)).ToList())).SingleOrDefault();
        foreach (var operation in new[] { "Merge", "Replace", "Stage" })
        {
            var command = owner.HasCommand(operation).Where<OptionalReferenceRequest>((row, request) => row.Id == request.OwnerId)
                .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                .ExpectVersion<int, OptionalReferenceRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
                .ReconcileLinks<OptionalReferenceRow, OptionalReferenceItem, int, OptionalReferenceRequest>(row => row.Links, request => request.Items,
                    row => row.TargetId, item => item.TargetId, links =>
                    {
                        links.TargetReference(row => row.Target).OptionalAdditionalReference(row => row.Reviewer).OptionalAdditionalReference(row => row.Source).MaximumItems(5)
                            .Add(item => new OptionalReferenceRow { TargetId = item.TargetId, ReviewerRegion = item.ReviewerRegion, ReviewerCode = item.ReviewerCode, SourceId = item.SourceId, Note = item.Note, Version = 1, Hidden = false })
                            .Update(setters => setters.SetProperty(row => row.Note, (row, item) => item.Note).SetProperty(row => row.Version, row => row.Version + 1))
                            .AllowWrite(row => row.Version).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                            .MissingItems(operation == "Merge" ? RepositoryCollectionRemovalBehavior.Keep : RepositoryCollectionRemovalBehavior.Delete);
                        change?.Invoke(links);
                    }).SimulateCollectionWrites(simulate);
            if (operation == "Stage") command.Stage();
            else command.Save().ReturnsSaved(row => new OptionalReferenceView(row.Id, row.Version,
                row.Links.Where(child => !child.Hidden).OrderBy(child => child.TargetId)
                    .Select(child => new OptionalReferenceData(child.OwnerId, child.TargetId, child.ReviewerRegion, child.ReviewerCode, child.SourceId, child.Note, child.Version, child.Hidden)).ToList()));
        }
        owner.HasCommand("StageName").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Name, "pending")).Stage();
        return builder;
    }

    internal static (object Repository, InMemoryWriteDataContext Data) Repositories(bool mapped = true)
    {
        var assembly = mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.OptionalReferenceContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped)
        {
            Store<OptionalReferenceOwnerData, int>(data, nameof(OptionalReferenceOwner)).SeedRange(Owners().Select(row => new OptionalReferenceOwnerData(row.Id, row.Name, row.Version)));
            Store<OptionalReferenceTargetData, int>(data, nameof(OptionalReferenceTarget)).SeedRange(Targets().Select(row => new OptionalReferenceTargetData(row.Id)));
            Store<OptionalReferenceReviewerData, int>(data, nameof(OptionalReferenceReviewer)).SeedRange(Reviewers().Select(row => new OptionalReferenceReviewerData(row.Id, row.Region, row.Code)));
            Store<OptionalReferenceSourceData, int>(data, nameof(OptionalReferenceSource)).SeedRange(Sources().Select(row => new OptionalReferenceSourceData(row.Id)));
            Store<OptionalReferenceData, (int, int)>(data, nameof(OptionalReferenceRow)).SeedRange(Links().Select(DataOf));
        }
        else
        {
            Store<OptionalReferenceOwner, int>(data, nameof(OptionalReferenceOwner)).SeedRange(Owners());
            Store<OptionalReferenceTarget, int>(data, nameof(OptionalReferenceTarget)).SeedRange(Targets());
            Store<OptionalReferenceReviewer, int>(data, nameof(OptionalReferenceReviewer)).SeedRange(Reviewers());
            Store<OptionalReferenceSource, int>(data, nameof(OptionalReferenceSource)).SeedRange(Sources());
            Store<OptionalReferenceRow, (int, int)>(data, nameof(OptionalReferenceRow)).SeedRange(Links());
        }
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.OptionalReferenceOwnerRepositoryTestDouble")!, fixture)!, data);
    }
    internal static OptionalReferenceOwner[] Owners() => [new() { Id = 1 }, new() { Id = 2, Name = "other" }];
    internal static OptionalReferenceTarget[] Targets() => Enumerable.Range(1, 6).Select(id => new OptionalReferenceTarget { Id = id }).ToArray();
    internal static OptionalReferenceReviewer[] Reviewers() => [new() { Id = 1, Region = 1, Code = "a" }, new() { Id = 2, Region = 2, Code = "b" }];
    internal static OptionalReferenceSource[] Sources() => [new() { Id = 1 }, new() { Id = 2 }];
    internal static OptionalReferenceRow[] Links() => [new() { OwnerId = 1, TargetId = 1, ReviewerRegion = 1, ReviewerCode = "a", SourceId = 1, Note = "present" },
        new() { OwnerId = 1, TargetId = 2, Note = "absent" }, new() { OwnerId = 1, TargetId = 6, ReviewerRegion = 2, ReviewerCode = "b", SourceId = 2, Note = "hidden", Hidden = true },
        new() { OwnerId = 2, TargetId = 1, ReviewerRegion = 2, ReviewerCode = "b", SourceId = 2, Note = "other" }];
    internal static OptionalReferenceData DataOf(OptionalReferenceRow row) => new(row.OwnerId, row.TargetId, row.ReviewerRegion, row.ReviewerCode, row.SourceId, row.Note, row.Version, row.Hidden);
    internal static InMemoryStore<T, TKey> Store<T, TKey>(InMemoryDataContext data, string name) where T : class where TKey : notnull => ManyReadTestHarness.Store<T, TKey>(data, name);
    internal static OptionalReferenceData[] Rows(InMemoryDataContext data, bool mapped) => (mapped ? Store<OptionalReferenceData, (int, int)>(data, nameof(OptionalReferenceRow)).Snapshot() :
        Store<OptionalReferenceRow, (int, int)>(data, nameof(OptionalReferenceRow)).Snapshot().Select(DataOf)).OrderBy(row => row.OwnerId).ThenBy(row => row.TargetId).ToArray();
}
