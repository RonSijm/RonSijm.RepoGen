using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class OptionalJoinTestHarness
{
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true, false));
    private static readonly Lazy<Assembly> EntityShaped = new(() => Compile(false, false));
    private static readonly Lazy<Assembly> BothMapped = new(() => Compile(true, true));
    private static readonly Lazy<Assembly> BothEntityShaped = new(() => Compile(false, true));

    internal static ManyReadContext Context(bool both = false, DbContextOptions<ManyReadContext>? options = null)
    {
        options ??= new DbContextOptionsBuilder<ManyReadContext>().UseSqlite("Data Source=:memory:").Options;
        return both ? new ManyReadBothOptionalContext(options) : new ManyReadOptionalContext(options);
    }

    private static Assembly Compile(bool mapped, bool both)
    {
        using var context = Context(both);
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped, both).Build()));
    }

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool both = false, bool queries = true)
    {
        var builder = ManyReadTestHarness.Builder(mapped, queries: queries);
        var owner = builder.Entity<ManyReadOwner>();
        foreach (var name in new[] { "Cards", "VisibleTags", "Search", "Page", "Stream", "Cycles", "Source" }) owner.ExcludeQuery(name);
        owner.ExcludeCommand("Rename").ExcludeCommand("RenameMany");
        owner.HasQuery("Cards").OrderBy(row => row.Id).Select(row => new ManyReadCard(row.Id, row.Name,
            row.Tags.Where(tag => tag != null).OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList())).List();
        owner.HasQuery("VisibleTags").OrderBy(row => row.Id).Select(row => row.Tags.Where(tag => tag != null && !tag.Hidden)
            .OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList()).List();
        owner.HasQuery("Search").Where<string>((row, name) => row.Tags.Where(tag => tag != null).Any(tag => tag.Name == name))
            .OrderBy(row => row.Id).Select(row => row.Id).List();
        owner.HasQuery("Counts").OrderBy(row => row.Id).Select(row => row.Tags.Where(tag => null != tag).Count()).List();
        owner.HasProjection("NonNullCount", row => row.Tags.Where(tag => tag != null).Count());
        owner.HasQuery("NamedCounts").OrderBy(row => row.Id).ProjectTo("NonNullCount").List();
        owner.HasQuery("Page").OrderBy(row => row.Id).Select(row => row.Tags.Where(tag => tag != null)
            .OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList()).Paged();
        owner.HasQuery("Stream").OrderBy(row => row.Id).Select(row => row.Tags.Where(tag => tag != null)
            .OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList()).Stream();
        owner.HasQuery("Source").From(sources => sources.Set<ManyReadOwner>().OrderBy(row => row.Id)
            .Select(row => row.Tags.Where(tag => tag != null).Count())).List();
        owner.HasQuery("RawCounts").OrderBy(row => row.Id).Select(row => row.Tags.Count)
            .RequireTestOverride("SQL retains unmatched/null targets; this operation deliberately has no non-null filter.").List();
        owner.HasCommand("Rename").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Name, "renamed")).Save()
            .ReturnsSaved(row => new ManyReadCard(row.Id, row.Name, row.Tags.Where(tag => tag != null).OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList()));
        owner.HasCommand("RenameMany").PatchMany<ManyReadPatch, int, IReadOnlyList<ManyReadPatch>>(request => request, row => row.Id, item => item.Id,
                setters => setters.SetProperty(row => row.Name, (row, item) => item.Name), maximumItems: 4).Save()
            .ReturnsSaved(row => new ManyReadCard(row.Id, row.Name, row.Tags.Where(tag => tag != null).OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList()));
        if (both)
        {
            builder.Entity<ManyReadTag>().ExcludeQuery("Owners").HasQuery("Owners").OrderBy(row => row.Id)
                .Select(row => new ManyReadTagCard(row.Id, row.Name, row.Owners.Where(item => item != null)
                    .OrderBy(item => item.Id).Select(item => item.Name).ToList())).List();
        }
        else
        {
            builder.Entity<ManyReadTag>().HasQuery("LoadedOwners").Include(row => row.Owners).OrderBy(row => row.Id)
                .Select(row => new ManyReadTagCard(row.Id, row.Name, row.Owners.OrderBy(item => item.Id).Select(item => item.Name).ToList())).List();
        }
        return builder;
    }

    internal static ManyReadLink[] Links(bool both = false) => [.. ManyReadTestHarness.Links(),
        new() { Id = 5, TenantId = 1, OwnerCode = "same", TagId = null },
        new() { Id = 6, TenantId = 1, OwnerCode = "same", TagId = null },
        .. both ? new[] { new ManyReadLink { Id = 7, TenantId = 1, OwnerCode = null!, TagId = 1 } } : []];

    internal static (object Owner, object Tag, InMemoryWriteDataContext Data) Repositories(bool mapped = true, bool both = false, Assembly? assembly = null)
    {
        assembly ??= both ? (mapped ? BothMapped.Value : BothEntityShaped.Value) : (mapped ? Mapped.Value : EntityShaped.Value);
        const string ns = "Generated.ManyRead.TestDoubles.";
        var contextName = both ? nameof(ManyReadBothOptionalContext) : nameof(ManyReadOptionalContext);
        var fixture = Activator.CreateInstance(assembly.GetType(ns + contextName + "RepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped)
        {
            ManyReadTestHarness.OwnerStore(data).SeedRange(ManyReadTestHarness.Owners().Select(row => new ManyReadOwnerData(row.TenantId, row.Id, row.Code, row.Name, row.Hidden)));
            ManyReadTestHarness.TagStore(data).SeedRange(ManyReadTestHarness.Tags().Select(row => new ManyReadTagData(row.Id, row.Name, row.Hidden)));
            ManyReadTestHarness.LinkStore(data).SeedRange(Links(both).Select(row => new ManyReadLinkData(row.Id, row.TenantId, row.OwnerCode, row.TagId, row.Hidden)));
        }
        else
        {
            ManyReadTestHarness.Store<ManyReadOwner, (int, int)>(data, nameof(ManyReadOwner)).SeedRange(ManyReadTestHarness.Owners());
            ManyReadTestHarness.Store<ManyReadTag, int>(data, nameof(ManyReadTag)).SeedRange(ManyReadTestHarness.Tags());
            ManyReadTestHarness.Store<ManyReadLink, int>(data, nameof(ManyReadLink)).SeedRange(Links(both));
        }
        return (Activator.CreateInstance(assembly.GetType(ns + "ManyReadOwnerRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(ns + "ManyReadTagRepositoryTestDouble")!, fixture)!, data);
    }
}
