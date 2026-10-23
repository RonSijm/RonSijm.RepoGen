using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;
using static RonSijm.RepoGen.Tool.Tests.NestedProjectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NestedProjectionContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActualGeneratedSqliteQueriesAndSavedCommandsAgreeWithFixtureRecipes(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new MemoryWriteContext(new DbContextOptionsBuilder<MemoryWriteContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(ProjectionTranslationTestHarness.Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, NestedProjectionTestHarness.Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MemoryWriteNoteRepositoryOperations")!, context)!;
        var (fake, _) = Fixture(mapped);
        async Task Equal<T>(string method, params object?[] input) =>
            JsonSerializer.Serialize(await Invoke<Task<T>>(fake, method, input)).Should().Be(JsonSerializer.Serialize(await Invoke<Task<T>>(real, method, input)), method);
        await Equal<IReadOnlyList<NestedProjectionCard>>("CardsAsync"); await Equal<IReadOnlyList<NestedProjectionCard>>("AllAsync");
        await Equal<NestedProjectionCard?>("FindAsync", 1); await Equal<NestedProjectionCard?>("FindAsync", 999);
        await Equal<IReadOnlyList<NestedProjectionCard>>("SourceAsync"); await Equal<IReadOnlyList<NestedProjectionCard>>("ArraysAsync");
        await Equal<IReadOnlyList<NestedProjectionCard>>("MaterializedAsync"); await Equal<IReadOnlyList<NestedProjectionEditable>>("EditableAsync");
        await Equal<IReadOnlyList<NestedProjectionStamp>>("StampsAsync"); await Equal<IReadOnlyList<NestedProjectionCard?>>("OptionalAsync");
        await Equal<IReadOnlyList<NestedProjectionCard>>("GroupsAsync"); await Equal<PagedResult<NestedProjectionCard>>("PageAsync", 2, 1);
        context.ChangeTracker.Entries().Should().BeEmpty();
        await Equal<CommandResult<NestedProjectionCard>>("RenameAsync", new ProjectionNotePatch(1, " renamed ", 1));
        await Equal<CommandResult<NestedProjectionCard>>("SavedRenameAsync", new ProjectionNotePatch(1, " saved ", 1));
        IReadOnlyList<ProjectionNotePatch> items = [new(2, "second", 2), new(1, "first", 1)];
        await Equal<CommandResult<IReadOnlyList<NestedProjectionCard>>>("RenameManyAsync", items);
    }

    [Fact]
    public async Task ARecipeDoesNotMakeAnOpaqueHelperSqlTranslatable()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new MemoryWriteContext(new DbContextOptionsBuilder<MemoryWriteContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(ProjectionTranslationTestHarness.Seeds()); await context.SaveChangesAsync();
        var builder = NestedProjectionTestHarness.Builder();
        builder.Entity<MemoryWriteNote>().HasQuery("HelperPredicate")
            .Where(row => NestedProjectionCalls.Card(row.Id, row.Title, row.Version, row.Hidden).Metrics.Count > 0).Select(row => row.Id).List();
        var design = builder.Build(); var realAssembly = SavedResultTestHarness.CompileProduction(context, design);
        var real = Activator.CreateInstance(realAssembly.GetType(Namespace + ".Persistence.MemoryWriteNoteRepositoryOperations")!, context)!;
        var fakeAssembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, design));
        var (fake, _) = Fixture(assembly: fakeAssembly);
        (await Invoke<Task<IReadOnlyList<int>>>(fake, "HelperPredicateAsync")).Should().Equal(1, 2);
        Func<Task> execute = async () => await Invoke<Task<IReadOnlyList<int>>>(real, "HelperPredicateAsync");
        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("*could not be translated*");
    }
}
