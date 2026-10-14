using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryCommandTranslationContractTests
{
    [Theory]
    [InlineData(false, "%OL%")]
    [InlineData(true, "%OL%")]
    [InlineData(false, "o_d")]
    [InlineData(true, "o_d")]
    [InlineData(false, "%")]
    [InlineData(true, "%")]
    [InlineData(false, "missing")]
    [InlineData(true, "missing")]
    public async Task RealSqliteAndFixturesSharePatternSelectionClrSettersSavedSqlProjectionAndStagedSave(bool mapped, string pattern)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new TranslatedWriteContext(new DbContextOptionsBuilder<TranslatedWriteContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(InMemoryCommandTranslationTestHarness.Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var builder = Builder(mapped); var design = builder.Build();
        var assembly = SavedResultTestHarness.CompileProduction(context, design);
        var real = Activator.CreateInstance(assembly.GetType(InMemoryCommandTranslationTestHarness.Namespace + ".Persistence.MemoryWriteNoteRepositoryOperations")!, context)!;
        var fakeAssembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, design));
        var fixture = Activator.CreateInstance(fakeAssembly.GetType(InMemoryCommandTranslationTestHarness.Namespace + ".Testing.TranslatedWriteContextRepositoryFixture")!)!;
        var fake = Activator.CreateInstance(fakeAssembly.GetType(InMemoryCommandTranslationTestHarness.Namespace + ".Testing.MemoryWriteNoteRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped) InMemoryCommandTranslationTestHarness.Store<TranslatedWriteSeed>(data).SeedRange(InMemoryCommandTranslationTestHarness.Seeds().Select(row => new TranslatedWriteSeed(row.Id, row.Title, row.Version, row.Hidden)));
        else InMemoryCommandTranslationTestHarness.Store<MemoryWriteNote>(data).SeedRange(InMemoryCommandTranslationTestHarness.Seeds());
        async Task<CommandResult<MemoryWriteView>> Equal(string method, TranslatedWriteRequest request)
        {
            var expected = await Invoke<CommandResult<MemoryWriteView>>(real, method, request);
            var actual = await Invoke<CommandResult<MemoryWriteView>>(fake, method, request);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected)); return actual;
        }
        var renamed = await Equal("RenameAsync", new(1, pattern, " new "));
        var version = renamed.Status == CommandStatus.Applied ? 2 : 1;
        await Equal("RenameAsync", new(2, "%", "hidden"));
        await Equal("RenameAsync", new(1, "%", "stale", 0));
        var stageRequest = new TranslatedWriteRequest(1, "%", " staged ", version);
        JsonSerializer.Serialize(await Invoke<CommandResult>(fake, "StageAsync", stageRequest)).Should().Be(JsonSerializer.Serialize(await Invoke<CommandResult>(real, "StageAsync", stageRequest)));
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync());
        await Equal("RenameAsync", new(1, "%", " final ", version + 1));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal("RenameAsync", new(1, "%", " rollback ", version + 2)); await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        var persisted = await context.Set<MemoryWriteNote>().OrderBy(row => row.Id).Select(row => new TranslatedWriteSeed(row.Id, row.Title, row.Version, row.Hidden)).ToListAsync();
        InMemoryCommandTranslationTestHarness.Rows(data, mapped).Should().Equal(persisted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedProjectionFailureDoesNotUndoTheCompletedWrite(bool mapped)
    {
        using var context = new TranslatedWriteContext(new DbContextOptionsBuilder<TranslatedWriteContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder(mapped);
        builder.Entity<MemoryWriteNote>().HasCommand("HideFromResult").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(row => row.Title, "")).Save().ReturnsSaved(row => TranslationCalls.Measure(row.Title)).UseInMemoryTranslations();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType(InMemoryCommandTranslationTestHarness.Namespace + ".Testing.TranslatedWriteContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType(InMemoryCommandTranslationTestHarness.Namespace + ".Testing.MemoryWriteNoteRepositoryTestDouble")!, fixture)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped) InMemoryCommandTranslationTestHarness.Store<TranslatedWriteSeed>(data).Seed(new(1, "old"));
        else InMemoryCommandTranslationTestHarness.Store<MemoryWriteNote>(data).Seed(new() { Id = 1, Title = "old" });
        Func<Task> fail = () => Invoke<CommandResult<int>>(repository, "HideFromResultAsync", 1);
        await fail.Should().ThrowAsync<RepositorySavedResultException>();
        InMemoryCommandTranslationTestHarness.Rows(data, mapped).Single().Title.Should().Be(""); data.PendingCount.Should().Be(0);
    }

    private static RepositoryModelBuilder Builder(bool mapped)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(InMemoryCommandTranslationTestHarness.Namespace + ".Contracts").UseGeneratedNamespace(InMemoryCommandTranslationTestHarness.Namespace + ".Persistence")
            .UseScriptedTestDoubles(InMemoryCommandTranslationTestHarness.Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands());
        InMemoryCommandTranslationTestHarness.Register(builder);
        var entity = builder.Entity<MemoryWriteNote>(); entity.ReadEnabled = false;
        if (mapped) entity.HasInMemoryStore<TranslatedWriteSeed, int>(row => row.Id, row => new(row.Id, row.Title, row.Version, row.Hidden))
            .MapInMemoryEntity<TranslatedWriteSeed>(row => new MemoryWriteNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden })
            .MapInMemoryWrites<TranslatedWriteSeed>((row, saved) => new(row.Id, row.Title, row.Version, row.Hidden));
        else entity.HasInMemoryStore(row => row.Id, row => new MemoryWriteNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden });
        entity.HasVisibilityPolicy("Visible", row => !row.Hidden && TranslationCalls.Measure(row.Title) > 0);
        foreach (var name in new[] { "Rename", "Stage" })
        {
            var command = entity.HasCommand(name).Where<TranslatedWriteRequest>((row, request) => row.Id == request.Id && EF.Functions.Like(row.Title, request.Pattern))
                .Validate<TranslatedWriteRequest>(request => request.Id > 0 && TranslatedWriteCalls.Normalize(request.Title).Length > 0, "A positive id and title are required.")
                .Patch(setters => setters.SetProperty<string, TranslatedWriteRequest>(row => row.Title, (row, request) => TranslatedWriteCalls.Normalize(request.Title))
                    .SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                .ExpectVersion<int, TranslatedWriteRequest>(row => row.Version, (row, request) => request.ExpectedVersion).UseInMemoryTranslations();
            if (name == "Stage") command.Stage();
            else command.Save().ReturnsSaved(row => new MemoryWriteView(row.Id, row.Title, TranslationCalls.Measure(row.Title)));
        }
        return builder;
    }
}
