using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class NativeSavedTestHarness
{
    internal const string Namespace = "Generated.NativeSaved";
    private static readonly Lazy<Assembly> Generated = new(() => Compile(Builder()));

    internal static RepositoryModelBuilder Builder(bool navigations = true, bool queries = true, bool backend = true, bool strict = true,
        bool scalarOnly = false, bool keyed = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseReturningWrites<SqliteReturningWriter>().UseScriptedTestDoubles(Namespace + ".Testing")
            .UseInMemoryTestStores().UseInMemoryTestQueries(queries).UseInMemoryTestCommands(backend).RequirePortableTestCommands(backend && strict));
        if (keyed)
        {
            builder.Configure(options => options.UseKeyedContextLeases<string>());
        }
        var parent = builder.Entity<MemoryCollectionParent>();
        parent.HasInMemoryStore(row => row.Id, row => new MemoryCollectionParent { Id = row.Id, OwnerId = row.OwnerId, Name = row.Name, Version = row.Version });
        var child = builder.Entity<MemoryCollectionChild>();
        child.HasInMemoryStore(row => row.Id, row => new MemoryCollectionChild
            { Id = row.Id, ParentId = row.ParentId, Key = row.Key, Text = row.Text, Version = row.Version, Hidden = row.Hidden });
        child.HasVisibilityPolicy("Visible", row => !row.Hidden);
        if (navigations)
        {
            parent.HasInMemoryNavigation(row => row.Children);
            child.HasInMemoryNavigation(row => row.Parent);
        }
        foreach (var name in scalarOnly ? new[] { "Scalar" } : new[] { "Rewrite", "TightBound", "Hide", "BypassHide", "Fail", "Scalar" })
        {
            var command = child.HasCommand(name).Where<NativeSavedRequest>((row, request) => row.ParentId == request.ParentId)
                .ExpectVersion<int, NativeSavedRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
                .Update(setters => setters.SetProperty<string, NativeSavedRequest>(row => row.Text, (row, request) => request.Text + row.Key)
                    .SetProperty<bool, NativeSavedRequest>(row => row.Hidden, (row, request) => request.Hidden)
                    .SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version);
            if (name == "Fail")
            {
                command.ReturnsSavedMany(row => new SavedResultRejectedView(row.Text), maximumRows: 5);
            }
            else if (name == "Scalar")
            {
                command.ReturnsSavedMany(row => row.Text, maximumRows: 5);
            }
            else
            {
                command.ReturnsSavedMany<NativeSavedView, NativeSavedRequest>((row, request) => new NativeSavedView(row.Id, row.Text, row.Version,
                    row.Parent == null ? "missing" : row.Parent.Name, request.Text,
                    row.Parent == null ? new List<string>() : row.Parent.Children.Where(sibling => !sibling.Hidden)
                        .OrderBy(sibling => sibling.Key).Select(sibling => sibling.Text).ToList()), maximumRows: name == "TightBound" ? 1 : 5);
            }
            if (name == "BypassHide")
            {
                command.BypassVisibility("Visible");
            }
            command.SimulateImmediateWrites();
            if (keyed)
            {
                command.UseContextKey<NativeSavedRequest, string>(request => request.Partition);
            }
        }
        if (!keyed)
        {
            parent.HasCommand("StageName").Where<int>((row, id) => row.Id == id)
                .Patch(setters => setters.SetProperty(row => row.Name, "pending parent")).Stage();
            child.HasCommand("StageText").Where<int>((row, id) => row.Id == id)
                .Patch(setters => setters.SetProperty(row => row.Text, "pending child")).Stage();
        }
        return builder;
    }

    internal static Assembly Compile(RepositoryModelBuilder builder)
    {
        using var context = MemoryCollectionTestHarness.Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    internal static Assembly CompileProduction(DbContext context, RepositoryModelBuilder builder, RepositoryOutputLayout layout = RepositoryOutputLayout.SingleFile)
    {
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        var sources = bundle.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(NativeSavedTestHarness).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("NativeSavedProduction_" + Guid.NewGuid().ToString("N"),
            sources.Select(file => CSharpSyntaxTree.ParseText(file.Contents)), references, ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        emitted.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
        output.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(output);
    }

    internal static (object Child, object Parent, InMemoryWriteDataContext Data) Fixture(Assembly? assembly = null, string? key = null)
    {
        assembly ??= Generated.Value;
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryCollectionContextRepositoryFixture")!)!;
        var data = ImmediateTestHarness.Data(fixture, key);
        MemoryCollectionTestHarness.ParentStore(data).SeedRange(Parents());
        MemoryCollectionTestHarness.ChildStore(data).SeedRange(Children());
        return (Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryCollectionChildRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.MemoryCollectionParentRepositoryTestDouble")!, fixture)!, data);
    }

    internal static MemoryCollectionParent[] Parents() => [new() { Id = 1, Name = "saved parent" }, new() { Id = 2, Name = "other" }];
    internal static MemoryCollectionChild[] Children() =>
    [
        new() { Id = 1, ParentId = 1, Key = "a", Text = "A" },
        new() { Id = 2, ParentId = 1, Key = "b", Text = "B" },
        new() { Id = 3, ParentId = 1, Key = "hidden", Text = "secret", Hidden = true },
        new() { Id = 4, ParentId = 2, Key = "c", Text = "C" }
    ];

    internal static Task<TResult> Call<TResult>(object repository, string operation, object request, CancellationToken token = default)
        => (Task<TResult>)repository.GetType().GetMethod(operation + "Async")!.Invoke(repository, [request, token])!;

    internal static async Task SeedAsync(DbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.AddRange(Parents());
        context.AddRange(Children());
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }
}
