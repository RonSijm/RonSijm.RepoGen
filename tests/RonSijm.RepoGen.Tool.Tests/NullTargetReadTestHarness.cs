using System.Reflection;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class NullTargetReadTestHarness
{
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true, false));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false, false));
    private static readonly Lazy<Assembly> BothMapped = new(() => Compile(true, true));
    private static readonly Lazy<Assembly> BothEntities = new(() => Compile(false, true));
    private static readonly string[] OldQueries = ["Cards", "VisibleTags", "Search", "Page", "Stream", "Cycles", "Source"];
    private static Assembly Compile(bool mapped, bool both)
    {
        using var context = OptionalJoinTestHarness.Context(both);
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped, both).Build()));
    }

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool both = false, bool preserve = true, bool queries = true, bool strict = true)
    {
        var builder = ManyReadTestHarness.Builder(mapped, queries: queries, strict: strict);
        var owner = builder.Entity<ManyReadOwner>();
        foreach (var name in OldQueries) owner.ExcludeQuery(name);
        owner.ExcludeCommand("Rename").ExcludeCommand("RenameMany");
        owner.HasInMemoryNavigation(row => row.Tags, preserveMissingTargets: preserve);
        owner.HasQuery("Cards").OrderBy(row => row.Id).Select(row => new NullTargetReadCard(row.Id, row.Name,
            row.Tags.Count, row.Tags.Count(tag => tag == null),
            row.Tags.OrderBy(tag => tag == null ? 0 : tag.Id).Select(tag => tag == null ? null : tag.Name).ToList())).List();
        owner.HasProjection("Missing", row => row.Tags.Count(tag => tag == null));
        owner.HasQuery("NamedCounts").OrderBy(row => row.Id).ProjectTo("Missing").List();
        owner.HasQuery("Counts").OrderBy(row => row.Id).Select(row => row.Tags.LongCount()).List();
        owner.HasQuery("Filtered").OrderBy(row => row.Id).Select(row => row.Tags.Where(tag => tag != null && !tag.Hidden)
            .OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList()).List();
        owner.HasQuery("HasMissing").Where(row => row.Tags.Any(tag => tag == null)).OrderBy(row => row.Id).Select(row => row.Id).List();
        owner.HasQuery("Search").Where<string>((row, name) => row.Tags.Any(tag => tag != null && tag.Name == name)).OrderBy(row => row.Id).Select(row => row.Id).List();
        owner.HasQuery("Page").OrderBy(row => row.Id).Select(row => new NullTargetReadCard(row.Id, row.Name,
            row.Tags.Count, row.Tags.Count(tag => tag == null),
            row.Tags.OrderBy(tag => tag == null ? 0 : tag.Id).Select(tag => tag == null ? null : tag.Name).ToList())).Paged();
        owner.HasQuery("Stream").OrderBy(row => row.Id).Select(row => new NullTargetReadCard(row.Id, row.Name,
            row.Tags.Count, row.Tags.Count(tag => tag == null),
            row.Tags.OrderBy(tag => tag == null ? 0 : tag.Id).Select(tag => tag == null ? null : tag.Name).ToList())).Stream();
        owner.HasQuery("Source").From(sources => sources.Set<ManyReadOwner>().OrderBy(row => row.Id).Select(row => row.Tags.Count(tag => tag == null))).List();
        owner.HasCommand("Rename").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Name, "renamed")).Save()
            .ReturnsSaved(row => new NullTargetReadCard(row.Id, row.Name, row.Tags.Count, row.Tags.Count(tag => tag == null),
                row.Tags.OrderBy(tag => tag == null ? 0 : tag.Id).Select(tag => tag == null ? null : tag.Name).ToList()));
        owner.HasCommand("RenameMany").PatchMany<ManyReadPatch, int, IReadOnlyList<ManyReadPatch>>(request => request, row => row.Id, item => item.Id,
                setters => setters.SetProperty(row => row.Name, (row, item) => item.Name), maximumItems: 4).Save()
            .ReturnsSaved(row => new NullTargetReadCard(row.Id, row.Name, row.Tags.Count, row.Tags.Count(tag => tag == null),
                row.Tags.OrderBy(tag => tag == null ? 0 : tag.Id).Select(tag => tag == null ? null : tag.Name).ToList()));
        if (both)
            builder.Entity<ManyReadTag>().ExcludeQuery("Owners").HasInMemoryNavigation(row => row.Owners, preserveMissingTargets: preserve)
                .HasQuery("Owners").OrderBy(row => row.Id).Select(row => row.Owners.Count(item => item == null)).List();
        return builder;
    }

    internal static (object Owner, object Tag, InMemoryWriteDataContext Data) Repositories(bool mapped = true, bool both = false, Assembly? assembly = null)
        => OptionalJoinTestHarness.Repositories(mapped, both, assembly ?? (both ? (mapped ? BothMapped.Value : BothEntities.Value) : (mapped ? Mapped.Value : Entities.Value)));
}
