using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class ManyReadTestHarness
{
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true));
    private static readonly Lazy<Assembly> EntityShaped = new(() => Compile(false));
    internal static ManyReadContext Context() => new(new DbContextOptionsBuilder<ManyReadContext>().UseSqlite("Data Source=:memory:").Options);
    private static Assembly Compile(bool mapped)
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }
    internal static RepositoryModelBuilder Builder(bool mapped = true, bool navigation = true, bool queries = true, bool strict = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.ManyRead.Contracts")
            .UseGeneratedNamespace("Generated.ManyRead.Persistence").UseScriptedTestDoubles("Generated.ManyRead.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestQueries(queries).RequirePortableTestQueries(strict && queries)
            .UseInMemoryTestCommands().RequirePortableTestCommands(strict && queries).GenerateOnlyConfiguredEntities());
        var owner = builder.Entity<ManyReadOwner>();
        owner.HasInMemoryStore(row => new ValueTuple<int, int>(row.TenantId, row.Id), row => new ManyReadOwner
            { TenantId = row.TenantId, Id = row.Id, Code = row.Code, Name = row.Name, Hidden = row.Hidden });
        var tag = builder.Entity<ManyReadTag>(); tag.WriteEnabled = false;
        tag.HasInMemoryStore(row => row.Id, row => new ManyReadTag { Id = row.Id, Name = row.Name, Hidden = row.Hidden });
        var link = builder.Entity<ManyReadLink>(); link.WriteEnabled = false;
        link.HasInMemoryStore(row => row.Id, row => new ManyReadLink { Id = row.Id, TenantId = row.TenantId, OwnerCode = row.OwnerCode, TagId = row.TagId, Hidden = row.Hidden });
        if (mapped)
        {
            owner.HasInMemoryStore<ManyReadOwnerData, (int, int)>(row => new ValueTuple<int, int>(row.TenantId, row.Id), row => new(row.TenantId, row.Id, row.Code, row.Name, row.Hidden))
                .MapInMemoryEntity<ManyReadOwnerData>(row => new ManyReadOwner { TenantId = row.TenantId, Id = row.Id, Code = row.Code, Name = row.Name, Hidden = row.Hidden })
                .MapInMemoryWrites<ManyReadOwnerData>((row, saved) => new(row.TenantId, row.Id, row.Code, row.Name, row.Hidden));
            tag.HasInMemoryStore<ManyReadTagData, int>(row => row.Id, row => new(row.Id, row.Name, row.Hidden))
                .MapInMemoryEntity<ManyReadTagData>(row => new ManyReadTag { Id = row.Id, Name = row.Name, Hidden = row.Hidden });
            link.HasInMemoryStore<ManyReadLinkData, int>(row => row.Id, row => new(row.Id, row.TenantId, row.OwnerCode, row.TagId, row.Hidden))
                .MapInMemoryEntity<ManyReadLinkData>(row => new ManyReadLink { Id = row.Id, TenantId = row.TenantId, OwnerCode = row.OwnerCode, TagId = row.TagId, Hidden = row.Hidden });
        }
        if (navigation) { owner.HasInMemoryNavigation(row => row.Tags); tag.HasInMemoryNavigation(row => row.Owners); }
        owner.HasVisibilityPolicy("Visible", row => !row.Hidden);
        tag.HasVisibilityPolicy("Visible", row => !row.Hidden);
        link.HasVisibilityPolicy("Visible", row => !row.Hidden);
        owner.HasQuery("Cards").OrderBy(row => row.Id).Select(row => new ManyReadCard(row.Id, row.Name, row.Tags.OrderBy(item => item.Id).Select(item => item.Name).ToList())).List();
        owner.HasQuery("VisibleTags").OrderBy(row => row.Id).Select(row => row.Tags.Where(item => !item.Hidden).OrderBy(item => item.Id).Select(item => item.Name).ToList()).List();
        owner.HasQuery("Search").Where<string>((row, name) => row.Tags.Any(item => item.Name == name)).OrderBy(row => row.Id).Select(row => row.Id).List();
        owner.HasQuery("Page").OrderBy(row => row.Id).Select(row => row.Tags.OrderBy(item => item.Id).Select(item => item.Name).ToList()).Paged();
        owner.HasQuery("Stream").OrderBy(row => row.Id).Select(row => row.Tags.OrderBy(item => item.Id).Select(item => item.Name).ToList()).Stream();
        owner.HasQuery("Cycles").OrderBy(row => row.Id).Load(load => load.Include(row => row.Tags).ThenInclude(row => row.Owners))
            .Select(row => row.Tags.Select(item => item.Owners.Count).ToList()).List();
        owner.HasQuery("Source").From(sources => sources.Set<ManyReadOwner>().OrderBy(row => row.Id)
            .Select(row => row.Tags.Count)).List();
        tag.HasQuery("Owners").OrderBy(row => row.Id).Select(row => new ManyReadTagCard(row.Id, row.Name, row.Owners.OrderBy(item => item.Id).Select(item => item.Name).ToList())).List();
        owner.HasCommand("Rename").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Name, "renamed")).Save()
            .ReturnsSaved(row => new ManyReadCard(row.Id, row.Name, row.Tags.OrderBy(item => item.Id).Select(item => item.Name).ToList()));
        owner.HasCommand("RenameMany").PatchMany<ManyReadPatch, int, IReadOnlyList<ManyReadPatch>>(request => request, row => row.Id, item => item.Id,
            setters => setters.SetProperty(row => row.Name, (row, item) => item.Name), maximumItems: 4).Save()
            .ReturnsSaved(row => new ManyReadCard(row.Id, row.Name, row.Tags.OrderBy(item => item.Id).Select(item => item.Name).ToList()));
        return builder;
    }
    internal static (object Owner, object Tag, InMemoryWriteDataContext Data) Repositories(bool mapped = true, Assembly? assembly = null)
    {
        assembly ??= mapped ? Mapped.Value : EntityShaped.Value;
        const string ns = "Generated.ManyRead.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "ManyReadContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped)
        {
            OwnerStore(data).SeedRange(Owners().Select(row => new ManyReadOwnerData(row.TenantId, row.Id, row.Code, row.Name, row.Hidden)));
            TagStore(data).SeedRange(Tags().Select(row => new ManyReadTagData(row.Id, row.Name, row.Hidden)));
            LinkStore(data).SeedRange(Links().Select(row => new ManyReadLinkData(row.Id, row.TenantId, row.OwnerCode, row.TagId, row.Hidden)));
        }
        else
        {
            Store<ManyReadOwner, (int, int)>(data, nameof(ManyReadOwner)).SeedRange(Owners());
            Store<ManyReadTag, int>(data, nameof(ManyReadTag)).SeedRange(Tags());
            Store<ManyReadLink, int>(data, nameof(ManyReadLink)).SeedRange(Links());
        }
        return (Activator.CreateInstance(assembly.GetType(ns + "ManyReadOwnerRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(ns + "ManyReadTagRepositoryTestDouble")!, fixture)!, data);
    }
    internal static ManyReadOwner[] Owners() =>
    [
        new() { TenantId = 1, Id = 1, Code = "same", Name = "North" }, new() { TenantId = 2, Id = 2, Code = "same", Name = "South" },
        new() { TenantId = 1, Id = 3, Code = "hidden", Name = "Hidden", Hidden = true }, new() { TenantId = 1, Id = 4, Code = "empty", Name = "Empty" }
    ];
    internal static ManyReadTag[] Tags() => [new() { Id = 1, Name = "Red" }, new() { Id = 2, Name = "Blue", Hidden = true }, new() { Id = 3, Name = "Unused" }];
    internal static ManyReadLink[] Links() =>
    [
        new() { Id = 1, TenantId = 1, OwnerCode = "same", TagId = 2, Hidden = true }, new() { Id = 2, TenantId = 1, OwnerCode = "same", TagId = 1 },
        new() { Id = 3, TenantId = 2, OwnerCode = "same", TagId = 2 }, new() { Id = 4, TenantId = 1, OwnerCode = "hidden", TagId = 1 }
    ];
    internal static InMemoryStore<T, TKey> Store<T, TKey>(InMemoryDataContext data, string name) where T : class where TKey : notnull
        => (InMemoryStore<T, TKey>)data.GetType().GetProperty(name + "Store")!.GetValue(data)!;
    internal static InMemoryStore<ManyReadOwnerData, (int, int)> OwnerStore(InMemoryDataContext data) => Store<ManyReadOwnerData, (int, int)>(data, nameof(ManyReadOwner));
    internal static InMemoryStore<ManyReadTagData, int> TagStore(InMemoryDataContext data) => Store<ManyReadTagData, int>(data, nameof(ManyReadTag));
    internal static InMemoryStore<ManyReadLinkData, int> LinkStore(InMemoryDataContext data) => Store<ManyReadLinkData, int>(data, nameof(ManyReadLink));
    internal static T Invoke<T>(object repository, string method, params object?[] arguments)
        => (T)repository.GetType().GetMethod(method)!.Invoke(repository, arguments.Append((object)CancellationToken.None).ToArray())!;
}
