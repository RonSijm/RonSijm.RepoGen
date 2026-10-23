using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedStateTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReturnContractTests
{
    [Theory]
    [InlineData("RenameReturned", 1, 3)]
    [InlineData("RenameReturned", 1, 99)]
    [InlineData("RenameReturned", 3, 3)]
    [InlineData("RenameReturned", 99, 3)]
    [InlineData("ConsumeReturned", 1, 3)]
    [InlineData("ConsumeReturned", 2, 5)]
    [InlineData("RenameAllReturned", 0, 0)]
    [InlineData("ConsumeAllReturned", 0, 0)]
    public async Task ActualGeneratedSqliteAndExplicitFixtureResultsAgree(string operation, int id, int version)
    {
        await using var context = Context();
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        foreach (var seed in Seeds())
        {
            var row = new MappedStateNote { Id = seed.Id, Title = seed.Title, Counter = seed.Counter };
            context.Add(row);
            context.Entry(row).Property(nameof(MappedStateNote.Caption)).CurrentValue = seed.Caption;
            context.Entry(row).Property("audit").CurrentValue = seed.Audit;
        }
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("UPDATE \"MappedStateNote\" SET \"OptionalVersion\" = NULL, \"Stamp\" = NULL WHERE \"Id\" = 2");
        context.ChangeTracker.Clear();
        var builder = MappedReturnTestHarness.Builder();
        var production = ImmediateTestHarness.CompileProduction(context, builder);
        var real = Activator.CreateInstance(production.GetType(Namespace + ".Persistence.MappedStateNoteRepositoryOperations")!, context, new SqliteReturningWriter())!;
        var (fake, _, _) = Fixture(MappedReturnTestHarness.Assembly);
        var request = new MappedStateRequest(id, "unused", version, id == 2 ? null : 1, id == 2 ? null : [1, 2]);
        var inputs = operation.Contains("All", StringComparison.Ordinal) ? Array.Empty<object?>() : [request];
        var expected = await Call<MutationResult<MappedStateCard>>(real, operation + "Async", inputs);
        var actual = await Call<MutationResult<MappedStateCard>>(fake, operation + "Async", inputs);
        actual.AffectedRows.Should().Be(expected.AffectedRows);
        JsonSerializer.Serialize(actual.Items.OrderBy(item => item.Id)).Should().Be(JsonSerializer.Serialize(expected.Items.OrderBy(item => item.Id)));
        context.ChangeTracker.Clear();
        JsonSerializer.Serialize(await Call<IReadOnlyList<MappedStateCard>>(fake, "CardsAsync"))
            .Should().Be(JsonSerializer.Serialize(await Call<IReadOnlyList<MappedStateCard>>(real, "CardsAsync")));
    }

    [Theory]
    [InlineData("FailUpdateReturned")]
    [InlineData("FailDeleteReturned")]
    public async Task ActualGeneratedSqliteAlsoRollsBackMultirowReturnedMappingFailure(string operation)
    {
        await using var context = Context();
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new MappedStateNote { Id = 1, Title = "old", Counter = 2 }, new MappedStateNote { Id = 2, Title = "other", Counter = 4 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var assembly = ImmediateTestHarness.CompileProduction(context, MappedReturnTestHarness.Builder());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MappedStateNoteRepositoryOperations")!, context, new SqliteReturningWriter())!;
        await Call<CommandResult>(repository, "StageRenameAsync", new MappedStateRequest(1, "pending"));
        Func<Task> execute = () => Call<MutationResult<MappedReturnFailure>>(repository, operation + "Async");
        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("Returned-value mapping failed*");
        var quantities = await context.Set<MappedStateNote>().AsNoTracking().OrderBy(row => row.Id).Select(row => row.Counter).ToListAsync();
        quantities.Should().Equal(2, 4);
        context.ChangeTracker.HasChanges().Should().BeTrue();
        await context.SaveChangesAsync();
        (await context.Set<MappedStateNote>().AsNoTracking().SingleAsync(row => row.Id == 1)).Title.Should().Be("pending");
    }
}
