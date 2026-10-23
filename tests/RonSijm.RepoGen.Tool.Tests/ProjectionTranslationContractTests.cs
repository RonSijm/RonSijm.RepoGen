using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ProjectionTranslationTestHarness;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProjectionTranslationContractTests
{
    [Fact]
    public async Task FixtureTranslationDoesNotIntroduceAProductionClientFilterFallback()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new MemoryWriteContext(new DbContextOptionsBuilder<MemoryWriteContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(ProjectionTranslationTestHarness.Seeds()); await context.SaveChangesAsync();
        var builder = ProjectionTranslationTestHarness.Builder();
        builder.Entity<MemoryWriteNote>().HasQuery("FilteredHelper")
            .Where(row => ProjectionNoteCalls.Card(row.Id, row.Title, row.Version, row.Hidden).Length > 0).Select(row => row.Id).List();
        var design = builder.Build(); var realAssembly = SavedResultTestHarness.CompileProduction(context, design);
        var real = Activator.CreateInstance(realAssembly.GetType(Namespace + ".Persistence.MemoryWriteNoteRepositoryOperations")!, context)!;
        var fakeAssembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, design));
        var (fake, _) = Fixture(assembly: fakeAssembly);
        (await Invoke<Task<IReadOnlyList<int>>>(fake, "FilteredHelperAsync")).Should().Equal(1, 2);
        Func<Task> execute = async () => await Invoke<Task<IReadOnlyList<int>>>(real, "FilteredHelperAsync");
        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("*could not be translated*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TopLevelDtoHelpersExecuteWithTheSameResultsOnGeneratedFixturesAndSqlite(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new MemoryWriteContext(new DbContextOptionsBuilder<MemoryWriteContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(ProjectionTranslationTestHarness.Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, ProjectionTranslationTestHarness.Builder(mapped).Build());
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MemoryWriteNoteRepositoryOperations")!, context)!;
        var (fake, _) = Fixture(mapped);
        async Task Equal<T>(string method, params object?[] input)
            => JsonSerializer.Serialize(await Invoke<Task<T>>(fake, method, input)).Should().Be(JsonSerializer.Serialize(await Invoke<Task<T>>(real, method, input)), method);
        await Equal<IReadOnlyList<ProjectionNoteCard>>("CardsAsync");
        await Equal<ProjectionNoteCard?>("GetAsync", 1); await Equal<ProjectionNoteCard?>("GetAsync", 999);
        await Equal<IReadOnlyList<ProjectionNoteData>>("MutableAsync"); await Equal<IReadOnlyList<ProjectionNoteStamp>>("StampsAsync");
        await Equal<IReadOnlyList<ProjectionNoteCard>>("SourceAsync"); await Equal<IReadOnlyList<ProjectionNoteCard>>("LiteralAsync");
        await Equal<IReadOnlyList<ProjectionNoteCard?>>("NullableAsync"); await Equal<IReadOnlyList<ProjectionNoteGroup>>("GroupsAsync");
        await Equal<PagedResult<ProjectionNoteCard>>("PageAsync", 2, 1); context.ChangeTracker.Entries().Should().BeEmpty();
        await Equal<CommandResult<ProjectionNoteCard>>("RenameAsync", new ProjectionNotePatch(1, " renamed ", 1));
        await Equal<CommandResult<ProjectionNoteCard>>("SavedRenameAsync", new ProjectionNotePatch(1, " saved ", 1));
        IReadOnlyList<ProjectionNotePatch> input = [new(2, "second", 2), new(1, "first", 1)];
        await Equal<CommandResult<IReadOnlyList<ProjectionNoteCard>>>("RenameManyAsync", input);
    }
}
