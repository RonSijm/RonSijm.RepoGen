using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class SavedResultTestHarness
{
    internal static RepositoryModelBuilder Builder(bool navigations = true, bool queries = true, bool strict = true, bool backend = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("Generated.SavedResults.Contracts").UseGeneratedNamespace("Generated.SavedResults.Persistence")
            .UseScriptedTestDoubles("Generated.SavedResults.TestDoubles").UseInMemoryTestStores()
            .UseInMemoryTestQueries(queries).UseInMemoryTestCommands(backend).RequirePortableTestCommands(strict && backend).GenerateOnlyConfiguredEntities());
        var parent = builder.Entity<MemoryCollectionParent>();
        parent.HasInMemoryStore(row => row.Id, row => new MemoryCollectionParent { Id = row.Id, OwnerId = row.OwnerId, Name = row.Name, Version = row.Version });
        if (navigations) parent.HasInMemoryNavigation(row => row.Children);
        foreach (var name in new[] { "Rename", "SaveGraph" })
        {
            var command = parent.HasCommand(name).Where<MemoryCollectionRequest>((row, request) => row.Id == request.Id && row.OwnerId == request.OwnerId)
                .Patch(setters => setters.SetProperty<string, MemoryCollectionRequest>(row => row.Name, (row, request) => request.Name)
                    .SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                .ExpectVersion<int, MemoryCollectionRequest>(row => row.Version, (row, request) => request.ExpectedVersion);
            if (name == "SaveGraph") command.ReconcileCollection<MemoryCollectionChild, MemoryCollectionItem, string, MemoryCollectionRequest>(
                row => row.Children, request => request.Items!, row => row.Key, item => item.Key,
                children => children.MaximumItems(5).AllowWrite(row => row.Version).Add(item => new MemoryCollectionChild { Key = item.Key, Text = item.Text, Version = 1, Hidden = false })
                    .Update(setters => setters.SetProperty(row => row.Text, (row, item) => item.Text))
                    .MissingItems(RepositoryCollectionRemovalBehavior.Delete)).SimulateCollectionWrites();
            command.Save().ReturnsSaved<SavedCollectionView, MemoryCollectionRequest>((row, request) => new SavedCollectionView(row.Id, row.Name, row.Version, request.Name,
                row.Children.Where(child => !child.Hidden).OrderBy(child => child.Key).Select(child => child.Text).ToList()));
        }
        parent.HasCommand("FailResult").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(row => row.Name, "committed")).Save().ReturnsSaved(row => new SavedResultRejectedView(row.Name));
        // The old predicate is deliberately false after the save: the result must use the primary key instead.
        parent.HasCommand("ChangePredicate").Where<int>((row, id) => row.Id == id && row.Name == "old")
            .Patch(setters => setters.SetProperty(row => row.Name, "changed")).Save().ReturnsSaved(row => row.Name);
        var child = builder.Entity<MemoryCollectionChild>();
        child.HasInMemoryStore(row => row.Id, row => new MemoryCollectionChild { Id = row.Id, ParentId = row.ParentId, Key = row.Key, Text = row.Text, Version = row.Version, Hidden = row.Hidden });
        child.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1);
        if (navigations) child.HasInMemoryNavigation(row => row.Parent);
        child.HasVisibilityPolicy("Visible", row => !row.Hidden);
        child.HasCommand("CreateChild").Create<MemoryCollectionRequest>(request => new MemoryCollectionChild
            { ParentId = request.Id, Key = request.Name, Text = "fresh", Hidden = false, Version = 1 }).AllowWrite(row => row.ParentId).AllowWrite(row => row.Version).Save()
            .ReturnsSaved(row => row.Parent == null ? "missing" : row.Parent.Name);
        child.HasCommand("StageText").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Text, "shared")).Stage();
        foreach (var bypass in new[] { false, true })
        {
            var command = child.HasCommand(bypass ? "BypassHide" : "Hide").Where<int>((row, id) => row.Id == id)
                .Patch(setters => setters.SetProperty(row => row.Hidden, true)).Save().ReturnsSaved(row => row.Text);
            if (bypass) command.BypassVisibility("Visible");
        }
        return builder;
    }

    internal static Assembly CompileProduction(DbContext context, RepositoryDesign design)
    {
        var model = RepositoryModelFactory.Create(context, typeof(MemoryCollectionContext).Assembly, design: design);
        var sources = new[] { RepositorySourceRenderer.Render(model), ClosedRepositorySourceRenderer.RenderContracts(model) };
        var references = ScriptedTestDoubleTestHarness.TestingReferences().Concat([typeof(MemoryCollectionContext).Assembly.Location, typeof(DbContext).Assembly.Location,
            typeof(RelationalQueryableExtensions).Assembly.Location, typeof(RonSijm.RepoGen.EntityFrameworkCore.TrackedCommandExecutor).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("SavedProduction_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)), references,
            ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream(); var emit = compilation.Emit(output);
        emit.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
        output.Position = 0; return AssemblyLoadContext.Default.LoadFromStream(output);
    }

    internal static (object Parent, object Child, InMemoryWriteDataContext Data) Repositories(RepositoryDesign? design = null)
    {
        using var context = MemoryCollectionTestHarness.Context();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, design ?? Builder().Build()));
        const string ns = "Generated.SavedResults.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "MemoryCollectionContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        MemoryCollectionTestHarness.ParentStore(data).SeedRange([new() { Id = 1, OwnerId = 10, Name = "old" }, new() { Id = 2, OwnerId = 20, Name = "other" }]);
        MemoryCollectionTestHarness.ChildStore(data).SeedRange([new() { Id = 1, ParentId = 1, Key = "a", Text = "A" }, new() { Id = 2, ParentId = 1, Key = "b", Text = "B" },
            new() { Id = 3, ParentId = 1, Key = "hidden", Text = "hidden", Hidden = true }, new() { Id = 4, ParentId = 2, Key = "other", Text = "other" }]);
        return (Activator.CreateInstance(assembly.GetType(ns + "MemoryCollectionParentRepositoryTestDouble")!, fixture)!,
            Activator.CreateInstance(assembly.GetType(ns + "MemoryCollectionChildRepositoryTestDouble")!, fixture)!, data);
    }
}
