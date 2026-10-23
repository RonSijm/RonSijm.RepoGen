using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class FilteredManyTestHarness
{
    private static readonly Lazy<Assembly> Mapped = new(() => Compile(true, false));
    private static readonly Lazy<Assembly> Entities = new(() => Compile(false, false));
    private static readonly Lazy<Assembly> OptionalMapped = new(() => Compile(true, true));
    private static readonly Lazy<Assembly> OptionalEntities = new(() => Compile(false, true));
    internal static ManyReadContext Context(bool optional = false)
    {
        var options = new DbContextOptionsBuilder<ManyReadContext>().UseSqlite("Data Source=:memory:").Options;
        return optional ? new FilteredManyOptionalContext(options) : new FilteredManyReadContext(options);
    }
    private static Assembly Compile(bool mapped, bool optional)
    {
        using var context = Context(optional);
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped, optional).Build()));
    }
    internal static RepositoryModelBuilder Builder(bool mapped = true, bool optional = false, bool filters = true, bool backend = true, bool strict = true)
    {
        var builder = optional ? NullTargetReadTestHarness.Builder(mapped, queries: backend, strict: strict)
            : ManyReadTestHarness.Builder(mapped, queries: backend, strict: strict);
        builder.Configure(options => options.UseInMemoryTestCommands(backend).RequirePortableTestCommands(strict && backend));
        var owner = builder.Entity<ManyReadOwner>();
        owner.UseInMemoryModelQueryFilter(filters, includeNavigations: true);
        builder.Entity<ManyReadTag>().UseInMemoryModelQueryFilter(filters, includeNavigations: true);
        builder.Entity<ManyReadLink>().UseInMemoryModelQueryFilter(filters, includeNavigations: true);
        owner.HasQuery("RootBypass").BypassVisibility("Visible").OrderBy(row => row.Id)
            .Select(row => row.Id).List();
        owner.HasQuery("All").IgnoreQueryFilters().BypassVisibility("Visible").OrderBy(row => row.Id)
            .Select(row => row.Tags.Where(tag => tag != null).OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList()).List();
        owner.ExcludeCommand("Rename").ExcludeCommand("RenameMany");
        var rename = owner.HasCommand("Rename").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(row => row.Name, "renamed")).UseInMemoryModelQueryFilter(filters, includeNavigations: true).Save();
        var batch = owner.HasCommand("RenameMany")
            .PatchMany<ManyReadPatch, int, IReadOnlyList<ManyReadPatch>>(input => input, row => row.Id, item => item.Id,
                setters => setters.SetProperty(row => row.Name, (row, item) => item.Name), maximumItems: 4)
            .UseInMemoryModelQueryFilter(filters, includeNavigations: true).Save();
        if (optional)
        {
            rename.ReturnsSaved(row => new NullTargetReadCard(row.Id, row.Name, row.Tags.Count, row.Tags.Count(tag => tag == null),
                row.Tags.OrderBy(tag => tag == null ? 0 : tag.Id).Select(tag => tag == null ? null : tag.Name).ToList()));
            batch.ReturnsSaved(row => new NullTargetReadCard(row.Id, row.Name, row.Tags.Count, row.Tags.Count(tag => tag == null),
                row.Tags.OrderBy(tag => tag == null ? 0 : tag.Id).Select(tag => tag == null ? null : tag.Name).ToList()));
        }
        else
        {
            rename.ReturnsSaved(row => new ManyReadCard(row.Id, row.Name, row.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList()));
            batch.ReturnsSaved(row => new ManyReadCard(row.Id, row.Name, row.Tags.OrderBy(tag => tag.Id).Select(tag => tag.Name).ToList()));
        }
        return builder;
    }
    internal static (object Owner, object Tag, InMemoryWriteDataContext Data) Fixture(bool mapped = true, bool optional = false, Assembly? assembly = null)
    {
        assembly ??= optional ? (mapped ? OptionalMapped.Value : OptionalEntities.Value) : (mapped ? Mapped.Value : Entities.Value);
        const string ns = "Generated.ManyRead.TestDoubles.";
        var contextName = optional ? nameof(FilteredManyOptionalContext) : nameof(FilteredManyReadContext);
        var fixture = Activator.CreateInstance(assembly.GetType(ns + contextName + "RepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var result = (Owner: Activator.CreateInstance(assembly.GetType(ns + "ManyReadOwnerRepositoryTestDouble")!, fixture)!,
            Tag: Activator.CreateInstance(assembly.GetType(ns + "ManyReadTagRepositoryTestDouble")!, fixture)!, Data: data);
        Seed(data, mapped);
        return result;
    }
    internal static void Seed(InMemoryWriteDataContext data, bool mapped)
    {
        if (mapped)
        {
            ManyReadTestHarness.OwnerStore(data).ReplaceWith(Owners().Select(row => new ManyReadOwnerData(row.TenantId, row.Id, row.Code, row.Name, row.Hidden)));
            ManyReadTestHarness.TagStore(data).ReplaceWith(Tags().Select(row => new ManyReadTagData(row.Id, row.Name, row.Hidden)));
            ManyReadTestHarness.LinkStore(data).ReplaceWith(Links().Select(row => new ManyReadLinkData(row.Id, row.TenantId, row.OwnerCode, row.TagId, row.Hidden)));
        }
        else
        {
            ManyReadTestHarness.Store<ManyReadOwner, (int, int)>(data, nameof(ManyReadOwner)).ReplaceWith(Owners());
            ManyReadTestHarness.Store<ManyReadTag, int>(data, nameof(ManyReadTag)).ReplaceWith(Tags());
            ManyReadTestHarness.Store<ManyReadLink, int>(data, nameof(ManyReadLink)).ReplaceWith(Links());
        }
    }
    internal static ManyReadOwner[] Owners() =>
    [
        new() { TenantId = 1, Id = 1, Code = "same", Name = "North" }, new() { TenantId = 2, Id = 2, Code = "same", Name = "South" },
        new() { TenantId = 1, Id = 3, Code = "archived", Name = "Archived owner" }, new() { TenantId = 1, Id = 4, Code = "empty", Name = "Empty" },
        new() { TenantId = 1, Id = 5, Code = "private", Name = "Private owner", Hidden = true }
    ];
    internal static ManyReadTag[] Tags() =>
    [
        new() { Id = 1, Name = "Red" }, new() { Id = 2, Name = "Archived tag", Hidden = true },
        new() { Id = 3, Name = "Unused" }, new() { Id = 4, Name = "Private tag", Hidden = true }
    ];
    internal static ManyReadLink[] Links() =>
    [
        new() { Id = 1, TenantId = 1, OwnerCode = "same", TagId = 2, Hidden = true },
        new() { Id = 2, TenantId = 1, OwnerCode = "same", TagId = 1 }, new() { Id = 3, TenantId = 2, OwnerCode = "same", TagId = 2 },
        new() { Id = 4, TenantId = 1, OwnerCode = "archived", TagId = 1 }, new() { Id = 5, TenantId = 1, OwnerCode = "same", TagId = 4 },
        new() { Id = 6, TenantId = 1, OwnerCode = "private", TagId = 1 }
    ];
}
