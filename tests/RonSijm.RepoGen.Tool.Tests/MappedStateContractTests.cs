using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedStateTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedStateContractTests
{
    [Theory]
    [InlineData("Rename")]
    [InlineData("StageRename")]
    [InlineData("RenameMany")]
    [InlineData("Create")]
    [InlineData("Remove")]
    [InlineData("ExecuteRename")]
    public async Task GeneratedSqliteAndMappedFixturesAgreeForExplicitShadowVersions(string operation)
    {
        await using var context = Context();
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        foreach (var seed in Seeds())
        {
            var entity = new MappedStateNote { Id = seed.Id, Title = seed.Title, Counter = seed.Counter };
            context.Add(entity);
            context.Entry(entity).Property(nameof(MappedStateNote.Caption)).CurrentValue = seed.Caption;
            context.Entry(entity).Property("audit").CurrentValue = seed.Audit;
        }
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("UPDATE \"MappedStateNote\" SET \"OptionalVersion\" = NULL, \"Stamp\" = NULL WHERE \"Id\" = 2");
        context.ChangeTracker.Clear();
        var design = Builder().Build();
        var production = SavedResultTestHarness.CompileProduction(context, design);
        var real = Activator.CreateInstance(production.GetType(Namespace + ".Persistence.MappedStateNoteRepositoryOperations")!, context)!;
        var (fake, data, _) = Fixture();
        var request = new MappedStateRequest(operation == "Create" ? 10 : 1, "changed");
        if (operation == "StageRename")
        {
            var expected = await Call<CommandResult>(real, operation + "Async", request);
            var actual = await Call<CommandResult>(fake, operation + "Async", request);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected));
            await context.SaveChangesAsync();
            await data.SaveChangesAsync();
        }
        else if (operation == "Remove")
        {
            JsonSerializer.Serialize(await Call<CommandResult>(fake, operation + "Async", request))
                .Should().Be(JsonSerializer.Serialize(await Call<CommandResult>(real, operation + "Async", request)));
        }
        else if (operation == "ExecuteRename")
        {
            (await Call<int>(fake, operation + "Async", request)).Should().Be(await Call<int>(real, operation + "Async", request));
        }
        else if (operation == "RenameMany")
        {
            IReadOnlyList<MappedStateRequest> input = [new(2, "two", 5), request];
            JsonSerializer.Serialize(await Call<CommandResult<IReadOnlyList<MappedStateCard>>>(fake, operation + "Async", input))
                .Should().Be(JsonSerializer.Serialize(await Call<CommandResult<IReadOnlyList<MappedStateCard>>>(real, operation + "Async", input)));
        }
        else
        {
            JsonSerializer.Serialize(await Call<CommandResult<MappedStateCard>>(fake, operation + "Async", request))
                .Should().Be(JsonSerializer.Serialize(await Call<CommandResult<MappedStateCard>>(real, operation + "Async", request)));
        }
        context.ChangeTracker.Clear();
        JsonSerializer.Serialize(await Call<IReadOnlyList<MappedStateCard>>(fake, "CardsAsync"))
            .Should().Be(JsonSerializer.Serialize(await Call<IReadOnlyList<MappedStateCard>>(real, "CardsAsync")));
    }
}
