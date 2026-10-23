using System.Linq.Expressions;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class MappedSetterTestHarness
{
    internal const string Namespace = "Generated.MappedSetters";
    private static readonly Lazy<Assembly> Production = new(() =>
    {
        using var context = Context();
        return CompileProduction(context, Builder());
    });
    private static readonly Lazy<Assembly> Testing = new(() =>
    {
        using var context = Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder().Build()));
    });
    internal static MappedSetterContext Context() => new(new DbContextOptionsBuilder<MappedSetterContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder Builder(bool state = true, bool backend = true, bool strict = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries(backend)
            .UseInMemoryTestCommands(backend).RequirePortableTestCommands(backend && strict));
        var note = builder.Entity<MappedSetterNote>();
        note.HasInMemoryStore<MappedSetterSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Caption, row.Audit, row.Version, row.Extra, row.UniqueCode, row.OwnerId))
            .MapInMemoryEntity<MappedSetterSeed>(row => new MappedSetterNote { Id = row.Id, Title = row.Title, UniqueCode = row.UniqueCode, OwnerId = row.OwnerId })
            .MapInMemoryProperty<MappedSetterSeed, string>(nameof(MappedSetterNote.Caption), row => row.Caption)
            .MapInMemoryProperty<MappedSetterSeed, int>("audit", row => row.Audit)
            .MapInMemoryProperty<MappedSetterSeed, int>(MappedSetterContext.Version, row => row.Version)
            .MapInMemoryProperty<MappedSetterSeed, string?>(MappedSetterContext.Extra, row => row.Extra);
        if (state)
            note.MapInMemoryWrites<MappedSetterSeed>((row, saved) => new(row.Id, row.Title, row.Caption, EF.Property<int>(row, "audit"),
                EF.Property<int>(row, MappedSetterContext.Version), (string?)row[MappedSetterContext.Extra], row.UniqueCode, row.OwnerId), useMappedPropertyState: true);
        note.HasVisibilityPolicy("Visible", row => row.Title != "hidden");
        note.HasQuery("Cards").OrderBy(row => row.Id).Select(Projection).List();
        foreach (var name in new[] { "Apply", "Stage", "Execute" })
        {
            var command = note.HasCommand(name).Where<MappedSetterRequest>((row, request) => row.Id == request.Id)
                .ExpectVersion<int, MappedSetterRequest>(MappedSetterContext.Version, request => request.Version)
                .AllowWrite<int>(MappedSetterContext.Version);
            void Set(RepositoryUpdateBuilder<MappedSetterNote> setters) => setters
                .SetProperty<string, MappedSetterRequest>(nameof(MappedSetterNote.Caption), (row, request) => request.Caption)
                .SetProperty<int>("audit", row => EF.Property<int>(row, "audit") + 1)
                .SetProperty<string?, MappedSetterRequest>(MappedSetterContext.Extra, (row, request) => request.Extra)
                .SetProperty<int, MappedSetterRequest>(MappedSetterContext.Version, (row, request) => request.Version + 1);
            if (name == "Execute") command.Update(Set).SimulateImmediateWrites();
            else
            {
                command.Patch(Set);
                if (name == "Stage") command.Stage(); else command.Save().ReturnsSaved(Projection);
            }
        }
        note.HasCommand("Conditional").Where<MappedSetterRequest>((row, request) => row.Id == request.Id)
            .Patch(setters => setters.SetPropertyWhen<string, MappedSetterRequest>(nameof(MappedSetterNote.Caption), (row, request) => request.Caption, request => request.ChangeCaption))
            .Save().ReturnsSaved(Projection);
        note.HasCommand("CurrentValues").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(nameof(MappedSetterNote.Caption), row => row.Caption + "!")
                .SetProperty<string?>(MappedSetterContext.Extra, row => (string?)row[MappedSetterContext.Extra] + "!"))
            .Save().ReturnsSaved(Projection);
        note.HasCommand("Break").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(row => row.Title, "temporary").SetProperty<string>(nameof(MappedSetterNote.Caption), row => null!))
            .Save().RequireTestOverride("A real-provider NOT NULL constraint is deliberately not simulated.");
        return builder;
    }

    internal static Expression<Func<MappedSetterNote, MappedSetterSeed>> Projection => row => new MappedSetterSeed(row.Id, row.Title, row.Caption,
        EF.Property<int>(row, "audit"), EF.Property<int>(row, MappedSetterContext.Version), (string?)row[MappedSetterContext.Extra], row.UniqueCode, row.OwnerId);

    internal static async Task SeedAsync(MappedSetterContext context)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        foreach (var seed in Seeds())
        {
            var row = new MappedSetterNote { Id = seed.Id, Title = seed.Title, UniqueCode = seed.UniqueCode };
            context.Add(row);
            context.Entry(row).Property(nameof(MappedSetterNote.Caption)).CurrentValue = seed.Caption;
            context.Entry(row).Property("audit").CurrentValue = seed.Audit;
            context.Entry(row).Property(MappedSetterContext.Version).CurrentValue = seed.Version;
            context.Entry(row).Property(MappedSetterContext.Extra).CurrentValue = seed.Extra;
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    internal static MappedSetterSeed[] Seeds() => [new(1, "old", "caption", 10, 3, "memo", "one"), new(2, "hidden", "other", 20, 4, null, "two")];
    internal static object Operations(MappedSetterContext context, RepositoryModelBuilder? builder = null)
    {
        var assembly = builder is null ? Production.Value : CompileProduction(context, builder);
        return Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MappedSetterNoteRepositoryOperations")!, context)!;
    }

    internal static Assembly CompileProduction(DbContext context, RepositoryModelBuilder builder, RepositoryOutputLayout layout = RepositoryOutputLayout.SingleFile)
    {
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        foreach (var source in bundle.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
        {
            var syntax = CSharpSyntaxTree.ParseText(source.Contents);
            var failure = syntax.GetDiagnostics().FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            if (failure is not null)
            {
                var line = failure.Location.GetLineSpan().StartLinePosition.Line;
                throw new InvalidOperationException(failure + "\n" + string.Join("\n", source.Contents.Split('\n').Skip(Math.Max(0, line - 1)).Take(3)));
            }
        }
        return NativeSavedTestHarness.CompileProduction(context, builder, layout);
    }

    internal static (object Repository, InMemoryWriteDataContext Data, InMemoryStore<MappedSetterSeed, int> Store) Fixture(Assembly? assembly = null)
    {
        assembly ??= Testing.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MappedSetterContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        var store = (InMemoryStore<MappedSetterSeed, int>)data.GetType().GetProperty("MappedSetterNoteStore")!.GetValue(data)!;
        store.SeedRange(Seeds());
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MappedSetterNoteRepositoryTestDouble")!, fixture)!, data, store);
    }
}
