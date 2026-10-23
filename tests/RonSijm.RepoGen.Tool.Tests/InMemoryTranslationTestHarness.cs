using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class InMemoryTranslationTestHarness
{
    internal const string Namespace = "Generated.Translations";
    private static readonly Lazy<Assembly> EntityAssembly = new(() => Compile(false));
    private static readonly Lazy<Assembly> MappedAssembly = new(() => Compile(true));
    private static Assembly Compile(bool mapped)
    {
        using var context = InMemoryQueryTestHarness.Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder(mapped).Build()));
    }
    internal static RepositoryModelBuilder Builder(bool mapped = false, bool translations = true, bool enabled = true, bool strict = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseClosedRepositories(Namespace + ".Contracts")
            .UseGeneratedNamespace(Namespace + ".Persistence").UseScriptedTestDoubles(Namespace + ".Testing")
            .UseInMemoryTestStores().UseInMemoryTestQueries(enabled).RequirePortableTestQueries(strict && enabled).GenerateOnlyConfiguredEntities());
        if (translations) Register(builder);
        var entity = builder.Entity<PortableRow>(); entity.WriteEnabled = false;
        if (mapped) entity.HasInMemoryStore<TranslationSeed, int>(row => row.Id, row => new TranslationSeed(row.Id, row.Name, row.Category, row.Price, row.Hidden))
            .MapInMemoryEntity<TranslationSeed>(row => new PortableRow { Id = row.Id, Name = row.Name, Category = row.Category, Price = row.Price, Hidden = row.Hidden });
        else entity.HasInMemoryStore(row => row.Id, row => new PortableRow { Id = row.Id, Name = row.Name, Category = row.Category, Price = row.Price, Hidden = row.Hidden });
        entity.HasVisibilityPolicy("Visible", row => !row.Hidden && TranslationCalls.Measure(row.Name) > 0);
        entity.HasDefaultSort(row => TranslationCalls.Measure(row.Name)).Descending();
        entity.HasQuery("Find").Where<string>((row, pattern) => EF.Functions.Like(row.Name, pattern)).OrderBy(row => row.Id).Select(row => row.Id).List();
        entity.HasQuery("Nested").Where<string>((row, pattern) => TranslationCalls.Echo(EF.Functions.Like(row.Name, pattern))).OrderBy(row => row.Id).Select(row => row.Id).List();
        entity.HasQuery("Default").Select(row => row.Id).List();
        entity.HasQuery("Grouped").GroupBy(row => TranslationCalls.Measure(row.Name), group => new TranslationGroup(group.Key, group.Sum(row => TranslationCalls.Shift(row.Price, 1))))
            .Results<TranslationGroup>().OrderBy(row => row.Length).List();
        entity.HasQuery("Pipeline").UseDefaultSort(false).Validate<string>(pattern => TranslationCalls.Measure(pattern) > 0, "A pattern is required.")
            .Select(row => new PortableView(row.Id, row.Name, row.Category, row.Price)).Where<string>((row, pattern) => EF.Functions.Like(row.Name, pattern))
            .OrderByDescending(row => TranslationCalls.Measure(row.Name)).ThenBy(row => row.Id)
            .TakeIf<int>(maximum => TranslationCalls.Positive(maximum), maximum => maximum).List();
        entity.HasQuery("FirstValue").Where<int>((row, id) => row.Id == id).ProjectTo(row => TranslationCalls.Shift(row.Price, 1)).SingleOrDefault();
        entity.HasQuery("AnnotatedValue").Where<int>((row, id) => row.Id == id).ProjectTo(row => TranslationCalls.Shift(row.Price, 1))
            .ResultNullability<int>(_ => { }).SingleOrDefault();
        entity.HasQuery("Dictionary").Results<PortableRow>().Dictionary(row => TranslationCalls.Echo(row.Id), row => TranslationCalls.Measure(row.Name));
        entity.HasQuery("Sum").Results<PortableRow>().Sum(row => TranslationCalls.Shift(row.Price, 1));
        entity.HasQuery("From").From(sources => sources.Set<PortableRow>().Where(row => EF.Functions.Like(row.Name, "%a%"))
            .Select(row => TranslationCalls.Shift(row.Price, 1)).OrderBy(value => value)).List();
        entity.HasQuery("Stream").Where<string>((row, pattern) => EF.Functions.Like(row.Name, pattern)).OrderBy(row => row.Id).Select(row => row.Id).Stream();
        entity.HasQuery("Cursor").OrderBy(row => row.Id).ProjectTo(row => TranslationCalls.Shift(row.Price, 1))
            .CursorPaged<int>((row, cursor) => TranslationCalls.Shift(row.Id, 0) > cursor, row => TranslationCalls.Shift(row.Id, 0));
        entity.HasQuery("Membership").WhereIn<int, PortableCriteria>(row => TranslationCalls.Shift(row.Id, 1), request => request.Ids).Select(row => row.Id).List();
        entity.HasQuery("FieldSort").Select(row => new PortableView(row.Id, row.Name, row.Category, row.Price))
            .OrderByField<PortableCriteria>(request => request.SortBy, request => request.Descending,
                fields => fields.Field("length", row => TranslationCalls.Measure(row.Name)).UseDefault("length")).ThenBy(row => row.Id).Paged();
        entity.HasQuery("Count").Count();
        entity.HasQuery("WrongFixed").RequireTestOverride("A different literal has no declared translation.").Select(row => TranslationCalls.Unit(row.Price, "different")).List();
        return builder;
    }
    internal static void Register(RepositoryModelBuilder builder)
    {
        builder.HasInMemoryTranslation<string, string, bool>("OrdinalIgnoreCasePatterns", (value, pattern) => EF.Functions.Like(value, pattern),
            (value, pattern) => InMemoryStringPatterns.Like(value, pattern, StringComparison.OrdinalIgnoreCase));
        builder.HasInMemoryTranslation<string, int>("Length", value => TranslationCalls.Measure(value), value => value.Length);
        builder.HasInMemoryTranslation<int, int, int>("Shift", (value, amount) => TranslationCalls.Shift(value, amount), (value, amount) => value + amount);
        builder.HasInMemoryTranslation<int, int>("IntegerEcho", value => TranslationCalls.Echo(value), value => value);
        builder.HasInMemoryTranslation<bool, bool>("BooleanEcho", value => TranslationCalls.Echo(value), value => value);
        builder.HasInMemoryTranslation<int, bool>("Positive", value => TranslationCalls.Positive(value), value => value > 0);
        builder.HasInMemoryTranslation<int, int>("FixedUnit", value => TranslationCalls.Unit(value, "one"), value => value);
    }
    internal static object Repository(bool mapped)
    {
        var assembly = (mapped ? MappedAssembly : EntityAssembly).Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.PortableReadContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.PortableRowRepositoryTestDouble")!, fixture)!;
        var data = fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = data.GetType().GetProperty("PortableRowStore")!.GetValue(data)!;
        if (mapped) ((InMemoryStore<TranslationSeed, int>)store).SeedRange(InMemoryQueryTestHarness.Seeds().Select(row => new TranslationSeed(row.Id, row.Name, row.Category, row.Price, row.Hidden)));
        else ((InMemoryStore<PortableRow, int>)store).SeedRange(InMemoryQueryTestHarness.Seeds());
        return repository;
    }
}
