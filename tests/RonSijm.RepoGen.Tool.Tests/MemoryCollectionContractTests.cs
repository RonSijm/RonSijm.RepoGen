using System.Runtime.Loader;
using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryCollectionContractTests
{
    [Fact]
    public async Task SameDesignerOutcomesAndChildVisibilityRunOnGeneratedSqliteAndFixtureRepositories()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new MemoryCollectionContext(new DbContextOptionsBuilder<MemoryCollectionContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new MemoryCollectionParent { Id = 1, OwnerId = 10, Name = "old" }, new MemoryCollectionParent { Id = 2, OwnerId = 20, Name = "other" });
        context.AddRange(new MemoryCollectionChild { Id = 1, ParentId = 1, Key = "a", Text = "old A" }, new MemoryCollectionChild { Id = 2, ParentId = 1, Key = "b", Text = "old B" },
            new MemoryCollectionChild { Id = 3, ParentId = 1, Key = "hidden", Text = "hidden", Hidden = true }, new MemoryCollectionChild { Id = 4, ParentId = 2, Key = "other", Text = "other" });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var model = RepositoryModelFactory.Create(context, typeof(MemoryCollectionContext).Assembly, design: new MemoryCollectionRepositoryDesigner().CreateModel());
        var sources = new[] { RepositorySourceRenderer.Render(model), ClosedRepositorySourceRenderer.RenderContracts(model) };
        var references = ScriptedTestDoubleTestHarness.TestingReferences().Concat([typeof(MemoryCollectionContext).Assembly.Location, typeof(DbContext).Assembly.Location,
            typeof(RelationalQueryableExtensions).Assembly.Location, typeof(RonSijm.RepoGen.EntityFrameworkCore.TrackedCommandExecutor).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("CollectionProduction_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)), references,
            ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream(); var emit = compilation.Emit(output);
        emit.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Should().BeEmpty();
        output.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(output);
        var realParent = Activator.CreateInstance(assembly.GetType("Generated.MemoryCollections.Persistence.MemoryCollectionParentRepositoryOperations")!, context)!;
        var realChild = Activator.CreateInstance(assembly.GetType("Generated.MemoryCollections.Persistence.MemoryCollectionChildRepositoryOperations")!, context)!;
        var (fakeParent, fakeChild, data) = MemoryCollectionTestHarness.Repositories();
        async Task Equal<TResult>(object real, object fake, string name, params object?[] inputs)
        {
            var actual = await Invoke<TResult>(fake, name, inputs); var expected = await Invoke<TResult>(real, name, inputs);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected), name);
        }
        async Task EqualState()
        {
            await Equal<MemoryCollectionView?>(realParent, fakeParent, "FindAsync", 1);
            await Equal<IReadOnlyList<string>>(realChild, fakeChild, "ListAsync", 1);
            await Equal<IReadOnlyList<string>>(realChild, fakeChild, "ListAsync", 2);
            var realRows = await context.Set<MemoryCollectionChild>().AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.ParentId, row.Key, row.Text, row.Version, row.Hidden }).ToListAsync();
            var fakeRows = MemoryCollectionTestHarness.ChildStore(data).Snapshot().OrderBy(row => row.Id).Select(row => new { row.Id, row.ParentId, row.Key, row.Text, row.Version, row.Hidden }).ToList();
            JsonSerializer.Serialize(fakeRows).Should().Be(JsonSerializer.Serialize(realRows));
        }
        await Equal<CommandResult<MemoryCollectionView>>(realParent, fakeParent, "SaveItemsAsync", new MemoryCollectionRequest(1, 10, "saved", [new("a", "updated"), new("n", "new")]));
        await EqualState();
        await Equal<CommandResult<MemoryCollectionView>>(realParent, fakeParent, "SaveItemsAsync", new MemoryCollectionRequest(1, 99, "wrong owner", []));
        await Equal<CommandResult<MemoryCollectionView>>(realParent, fakeParent, "SaveItemsAsync", new MemoryCollectionRequest(1, 10, "stale parent", [], 1));
        await Equal<CommandResult<MemoryCollectionView>>(realParent, fakeParent, "SaveItemsAsync", new MemoryCollectionRequest(1, 10, "stale child", [new("a", "stale", 1)], 2));
        await EqualState();
        await Equal<CommandResult<MemoryCollectionView>>(realParent, fakeParent, "KeepItemsAsync", new MemoryCollectionRequest(1, 10, "keep", [new("a", "A", 2)], 2));
        await EqualState();
        await Equal<CommandResult>(realParent, fakeParent, "OptionalItemsAsync", new MemoryCollectionOptionalRequest(1, default));
        await Equal<CommandResult>(realParent, fakeParent, "StageItemsAsync", new MemoryCollectionRequest(1, 10, "staged", [], 3));
        await Equal<CommandResult>(realChild, fakeChild, "StageTextAsync", 4);
        await EqualState();
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync());
        await EqualState();
        await using (var realTransaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal<CommandResult<MemoryCollectionView>>(realParent, fakeParent, "SaveItemsAsync", new MemoryCollectionRequest(1, 10, "rollback", [new("r", "rollback")], 4));
            // SQLite identity allocation after deleted maxima is provider-specific, not a fixture policy.
            await Equal<IReadOnlyList<string>>(realChild, fakeChild, "ListAsync", 1);
            await realTransaction.RollbackAsync();
        }
        context.ChangeTracker.Clear(); await EqualState(); data.PendingCount.Should().Be(0);
    }
}
