using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class LinkWriteTestHarness
{
    internal const string Namespace = "Generated.LinkWrites";
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false));

    internal static LinkWriteContext Context() => new(new DbContextOptionsBuilder<LinkWriteContext>().UseSqlite("Data Source=:memory:").Options);
    private static Assembly Compile(bool mapped)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool simulate = true, bool backend = true, bool strict = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".TestDoubles").UseInMemoryTestStores().UseInMemoryTestQueries()
            .UseInMemoryTestCommands(backend).RequirePortableTestQueries().RequirePortableTestCommands(strict && backend));
        var owner = builder.Entity<LinkWriteOwner>();
        var target = builder.Entity<LinkWriteTarget>(); target.WriteEnabled = false;
        var link = builder.Entity<LinkWriteRow>();
        if (mapped)
        {
            owner.HasInMemoryStore<LinkWriteOwnerData, (int, int)>(row => new ValueTuple<int, int>(row.TenantId, row.Id), row => new(row.TenantId, row.Id, row.Name, row.Version, row.Hidden))
                .MapInMemoryEntity<LinkWriteOwnerData>(row => new LinkWriteOwner { TenantId = row.TenantId, Id = row.Id, Name = row.Name, Version = row.Version, Hidden = row.Hidden })
                .MapInMemoryWrites<LinkWriteOwnerData>((row, saved) => new(row.TenantId, row.Id, row.Name, row.Version, row.Hidden));
            target.HasInMemoryStore<LinkWriteTargetData, (string, string)>(row => new ValueTuple<string, string>(row.Region, row.Code), row => new(row.Region, row.Code, row.Name, row.Hidden))
                .MapInMemoryEntity<LinkWriteTargetData>(row => new LinkWriteTarget { Region = row.Region, Code = row.Code, Name = row.Name, Hidden = row.Hidden });
            link.HasInMemoryStore<LinkWriteRowData, (int, int, string, string)>(row => new ValueTuple<int, int, string, string>(row.OwnerTenantId, row.OwnerId, row.TargetRegion, row.TargetCode), row => new(row.OwnerTenantId, row.OwnerId, row.TargetRegion, row.TargetCode, row.Note, row.Version, row.Hidden))
                .MapInMemoryEntity<LinkWriteRowData>(row => new LinkWriteRow { OwnerTenantId = row.OwnerTenantId, OwnerId = row.OwnerId, TargetRegion = row.TargetRegion, TargetCode = row.TargetCode, Note = row.Note, Version = row.Version, Hidden = row.Hidden })
                .MapInMemoryWrites<LinkWriteRowData>((row, saved) => new(row.OwnerTenantId, row.OwnerId, row.TargetRegion, row.TargetCode, row.Note, row.Version, row.Hidden));
        }
        else
        {
            owner.HasInMemoryStore(row => new ValueTuple<int, int>(row.TenantId, row.Id), row => new LinkWriteOwner { TenantId = row.TenantId, Id = row.Id, Name = row.Name, Version = row.Version, Hidden = row.Hidden });
            target.HasInMemoryStore(row => new ValueTuple<string, string>(row.Region, row.Code), row => new LinkWriteTarget { Region = row.Region, Code = row.Code, Name = row.Name, Hidden = row.Hidden });
            link.HasInMemoryStore(row => new ValueTuple<int, int, string, string>(row.OwnerTenantId, row.OwnerId, row.TargetRegion, row.TargetCode), row => new LinkWriteRow { OwnerTenantId = row.OwnerTenantId, OwnerId = row.OwnerId, TargetRegion = row.TargetRegion, TargetCode = row.TargetCode, Note = row.Note, Version = row.Version, Hidden = row.Hidden });
        }
        owner.HasVisibilityPolicy("Visible", row => !row.Hidden);
        link.HasVisibilityPolicy("Visible", row => !row.Hidden);
        target.HasVisibilityPolicy("Visible", row => !row.Hidden);
        owner.HasInMemoryNavigation(row => row.Tags); target.HasInMemoryNavigation(row => row.Owners);
        owner.HasQuery("Find").Where<LinkWriteRequest>((row, request) => row.TenantId == request.TenantId && row.Id == request.OwnerId)
            .Select(row => new LinkWriteView(row.TenantId, row.Id, row.Version, row.Tags.OrderBy(tag => tag.Region).ThenBy(tag => tag.Code).Select(tag => tag.Name).ToList())).SingleOrDefault();
        target.HasQuery("Usage").OrderBy(row => row.Region).ThenBy(row => row.Code).Select(row => row.Owners.Count).List();
        foreach (var name in new[] { "Replace", "Merge", "Reject", "Stage" })
        {
            var command = owner.HasCommand(name).Where<LinkWriteRequest>((row, request) => row.TenantId == request.TenantId && row.Id == request.OwnerId)
                .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                .ExpectVersion<int, LinkWriteRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
                .ReconcileLinks<LinkWriteRow, LinkWriteItem, (string, string), LinkWriteRequest>(row => row.Links, request => request.Items!,
                    row => new ValueTuple<string, string>(row.TargetRegion, row.TargetCode), item => new ValueTuple<string, string>(item.Region, item.Code),
                    links => links.MaximumItems(4).Add(item => new LinkWriteRow { TargetRegion = item.Region, TargetCode = item.Code, Note = item.Note, Version = 1, Hidden = false })
                        .Update(setters => setters.SetProperty(row => row.Note, (row, item) => item.Note).SetProperty(row => row.Version, row => row.Version + 1))
                        .AllowWrite(row => row.Version).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                        .MissingItems(name == "Merge" ? RepositoryCollectionRemovalBehavior.Keep : name == "Reject" ? RepositoryCollectionRemovalBehavior.Reject : RepositoryCollectionRemovalBehavior.Delete))
                .SimulateCollectionWrites(simulate);
            if (name == "Stage") command.Stage();
            else command.Save().ReturnsSaved(row => new LinkWriteView(row.TenantId, row.Id, row.Version,
                row.Tags.OrderBy(tag => tag.Region).ThenBy(tag => tag.Code).Select(tag => tag.Name).ToList()));
        }
        owner.HasCommand("Optional").Where<LinkWriteOptionalRequest>((row, request) => row.TenantId == request.TenantId && row.Id == request.OwnerId)
            .ReconcileLinks<LinkWriteRow, LinkWriteItem, (string, string), LinkWriteOptionalRequest>(row => row.Links, request => request.Items.Value!,
                row => new ValueTuple<string, string>(row.TargetRegion, row.TargetCode), item => new ValueTuple<string, string>(item.Region, item.Code),
                links => links.MaximumItems(4).When<LinkWriteOptionalRequest>(request => request.Items.IsSpecified).MissingItems(RepositoryCollectionRemovalBehavior.Delete))
            .Save().SimulateCollectionWrites(simulate);
        owner.HasCommand("StageName").Where<int>((row, tenant) => row.TenantId == tenant).Patch(setters => setters.SetProperty(row => row.Name, "pending")).Stage();
        return builder;
    }

    internal static (object Owner, object Target, InMemoryWriteDataContext Data) Repositories(bool mapped = true)
    {
        var assembly = mapped ? Mapped.Value : Entities.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.LinkWriteContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped)
        {
            OwnerStore(data).SeedRange(Owners().Select(row => new LinkWriteOwnerData(row.TenantId, row.Id, row.Name, row.Version, row.Hidden)));
            TargetStore(data).SeedRange(Targets().Select(row => new LinkWriteTargetData(row.Region, row.Code, row.Name, row.Hidden)));
            LinkStore(data).SeedRange(Links().Select(row => new LinkWriteRowData(row.OwnerTenantId, row.OwnerId, row.TargetRegion, row.TargetCode, row.Note, row.Version, row.Hidden)));
        }
        else { Store<LinkWriteOwner, (int, int)>(data, nameof(LinkWriteOwner)).SeedRange(Owners()); Store<LinkWriteTarget, (string, string)>(data, nameof(LinkWriteTarget)).SeedRange(Targets()); Store<LinkWriteRow, (int, int, string, string)>(data, nameof(LinkWriteRow)).SeedRange(Links()); }
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.LinkWriteOwnerRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.LinkWriteTargetRepositoryTestDouble")!, fixture)!, data);
    }
    internal static LinkWriteOwner[] Owners() => [new() { TenantId = 1, Id = 1, Name = "first" }, new() { TenantId = 2, Id = 1, Name = "other" }];
    internal static LinkWriteTarget[] Targets() => [new() { Region = "eu", Code = "a", Name = "A" }, new() { Region = "eu", Code = "b", Name = "B" }, new() { Region = "us", Code = "a", Name = "US A" }, new() { Region = "eu", Code = "h", Name = "Hidden target", Hidden = true }];
    internal static LinkWriteRow[] Links() => [new() { OwnerTenantId = 1, OwnerId = 1, TargetRegion = "eu", TargetCode = "a", Note = "A" },
        new() { OwnerTenantId = 1, OwnerId = 1, TargetRegion = "eu", TargetCode = "b", Note = "B" },
        new() { OwnerTenantId = 1, OwnerId = 1, TargetRegion = "eu", TargetCode = "h", Note = "hidden", Hidden = true },
        new() { OwnerTenantId = 2, OwnerId = 1, TargetRegion = "eu", TargetCode = "a", Note = "other" }];
    internal static InMemoryStore<T, TKey> Store<T, TKey>(InMemoryDataContext data, string entity) where T : class where TKey : notnull => ManyReadTestHarness.Store<T, TKey>(data, entity);
    internal static InMemoryStore<LinkWriteOwnerData, (int, int)> OwnerStore(InMemoryDataContext data) => Store<LinkWriteOwnerData, (int, int)>(data, nameof(LinkWriteOwner));
    internal static InMemoryStore<LinkWriteTargetData, (string, string)> TargetStore(InMemoryDataContext data) => Store<LinkWriteTargetData, (string, string)>(data, nameof(LinkWriteTarget));
    internal static InMemoryStore<LinkWriteRowData, (int, int, string, string)> LinkStore(InMemoryDataContext data) => Store<LinkWriteRowData, (int, int, string, string)>(data, nameof(LinkWriteRow));
    internal static LinkWriteRowData[] Rows(InMemoryDataContext data, bool mapped) => (mapped ? LinkStore(data).Snapshot() : Store<LinkWriteRow, (int, int, string, string)>(data, nameof(LinkWriteRow)).Snapshot()
        .Select(row => new LinkWriteRowData(row.OwnerTenantId, row.OwnerId, row.TargetRegion, row.TargetCode, row.Note, row.Version, row.Hidden))).OrderBy(row => row.OwnerTenantId).ThenBy(row => row.TargetRegion).ThenBy(row => row.TargetCode).ToArray();
}
