using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedCollectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedCollectionContractTests
{
    [Theory]
    [InlineData("Replace")]
    [InlineData("Merge")]
    [InlineData("Stage")]
    [InlineData("Saved")]
    [InlineData("StaleParent")]
    [InlineData("StaleChild")]
    [InlineData("NullVersions")]
    [InlineData("Reject")]
    public async Task ActualGeneratedSqliteAndMappedParticipantFixturesAgree(string operation)
    {
        await using var context = Context();
        await SeedAsync(context);
        var assembly = SavedResultTestHarness.CompileProduction(context, Builder().Build());
        var realParent = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MappedCollectionParentRepositoryOperations")!, context)!;
        var realChild = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MappedCollectionChildRepositoryOperations")!, context)!;
        var (fakeParent, fakeChild, data) = Fixture();
        var request = Request();
        var method = operation is "StaleParent" or "StaleChild" or "NullVersions" ? "Replace" : operation;
        request = operation switch
        {
            "StaleParent" => request with { Revision = 99 },
            "StaleChild" => request with { Items = [request.Items[0] with { Revision = 99 }, request.Items[1]] },
            "NullVersions" => request with { Items = [new(2, "b", "null tokens", 5, null, null)] },
            _ => request
        };
        if (operation == "Reject")
        {
            Func<Task> real = () => Call<CommandResult<int>>(realParent, method, request);
            Func<Task> fake = () => Call<CommandResult<int>>(fakeParent, method, request);
            await real.Should().ThrowAsync<ArgumentException>();
            await fake.Should().ThrowAsync<ArgumentException>();
        }
        else if (operation == "Saved")
        {
            var expected = await Call<CommandResult<MappedCollectionView>>(realParent, method, request);
            var actual = await Call<CommandResult<MappedCollectionView>>(fakeParent, method, request);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected));
        }
        else if (operation == "Stage")
        {
            JsonSerializer.Serialize(await Call<CommandResult>(fakeParent, method, request))
                .Should().Be(JsonSerializer.Serialize(await Call<CommandResult>(realParent, method, request)));
            await context.SaveChangesAsync();
            await data.SaveChangesAsync();
        }
        else
        {
            JsonSerializer.Serialize(await Call<CommandResult<int>>(fakeParent, method, request))
                .Should().Be(JsonSerializer.Serialize(await Call<CommandResult<int>>(realParent, method, request)));
        }
        context.ChangeTracker.Clear();
        JsonSerializer.Serialize(await Call<IReadOnlyList<MappedCollectionCard>>(fakeChild, "Cards"))
            .Should().Be(JsonSerializer.Serialize(await Call<IReadOnlyList<MappedCollectionCard>>(realChild, "Cards")));
        JsonSerializer.Serialize(await Call<IReadOnlyList<int>>(fakeParent, "Versions"))
            .Should().Be(JsonSerializer.Serialize(await Call<IReadOnlyList<int>>(realParent, "Versions")));
    }

    internal static async Task SeedAsync(MappedCollectionContext context)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new MappedCollectionParent { Id = 1, Title = "old", Counter = 2 },
            new MappedCollectionParent { Id = 2, Title = "other", Counter = 4 });
        foreach (var seed in Seeds())
        {
            var row = new MappedCollectionChild { Id = seed.Id, ParentId = seed.ParentId, Code = seed.Code, Text = seed.Text, Counter = seed.Counter };
            context.Add(row);
            context.Entry(row).Property(nameof(MappedCollectionChild.Category)).CurrentValue = seed.Category;
            context.Entry(row).Property("audit").CurrentValue = seed.Audit;
        }
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("UPDATE \"MappedCollectionChild\" SET \"OptionalVersion\" = NULL, \"Stamp\" = NULL WHERE \"Id\" = 2");
        context.ChangeTracker.Clear();
    }
}
