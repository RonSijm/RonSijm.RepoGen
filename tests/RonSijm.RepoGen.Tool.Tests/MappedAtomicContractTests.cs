using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedAtomicTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedAtomicContractTests
{
    private static readonly Lazy<Assembly> Production = new(() =>
    {
        using var context = Context();
        return ImmediateTestHarness.CompileProduction(context, Builder());
    });

    [Theory]
    [InlineData("insert")]
    [InlineData("existing")]
    [InlineData("update")]
    [InlineData("stale")]
    [InlineData("null")]
    [InlineData("hidden")]
    [InlineData("filtered")]
    [InlineData("insertFiltered")]
    [InlineData("filterChanged")]
    [InlineData("failInsert")]
    [InlineData("failUpdate")]
    public async Task ActualGeneratedSqliteAndFixturesAgreeOnCapturedScalarsAndBranchBoundaries(string scenario)
    {
        await using var context = Context(); await SeedAsync(context);
        var real = Activator.CreateInstance(Production.Value.GetType(Namespace + ".Persistence.MappedStateNoteRepositoryOperations")!, context, new SqliteAtomicWriter())!;
        var (fake, _, _) = Fixture();
        var operation = scenario switch { "insert" or "existing" or "insertFiltered" => "Ensure", "failInsert" => "FailEnsure", "failUpdate" => "FailPut", _ => "Put" };
        var request = scenario switch
        {
            "insert" or "failInsert" => new MappedAtomicRequest(20, "new ' quoted", 4),
            "insertFiltered" => new(20, "outside filter", 10),
            "null" => new(900, "other", Revision: 5, OptionalVersion: null),
            "hidden" => new(900, "hidden", Stamp: [1, 2]),
            "filtered" => new(900, "filtered", Revision: 11, Stamp: [1, 2]),
            "stale" => new(900, "one", Revision: 99, Stamp: [1, 2]),
            "filterChanged" => new(900, "one", 7, Stamp: [1, 2]),
            _ => new(900, "one", 2, Stamp: [1, 2])
        };
        MappedStateNote.GetterCalls.Value = 0;
        if (scenario.StartsWith("fail", StringComparison.Ordinal))
        {
            Func<Task> production = () => Call<CommandResult<MappedAtomicFailure>>(real, operation, request);
            Func<Task> fixture = () => Call<CommandResult<MappedAtomicFailure>>(fake, operation, request);
            await production.Should().ThrowAsync<InvalidOperationException>().WithMessage("atomic mapped result failed:*");
            await fixture.Should().ThrowAsync<InvalidOperationException>().WithMessage("atomic mapped result failed:*");
        }
        else if (scenario == "filterChanged")
        {
            Func<Task> production = () => Call<CommandResult<MappedStateCard>>(real, operation, request);
            Func<Task> fixture = () => Call<CommandResult<MappedStateCard>>(fake, operation, request);
            await production.Should().ThrowAsync<InvalidOperationException>();
            await fixture.Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            var expected = await Call<CommandResult<MappedStateCard>>(real, operation, request);
            var actual = await Call<CommandResult<MappedStateCard>>(fake, operation, request);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected));
        }
        MappedStateNote.GetterCalls.Value.Should().Be(0);
        context.ChangeTracker.Entries().Should().BeEmpty();
        var expectedRows = await MappedStateTestHarness.Call<IReadOnlyList<MappedStateCard>>(real, "CardsAsync");
        var actualRows = await MappedStateTestHarness.Call<IReadOnlyList<MappedStateCard>>(fake, "CardsAsync");
        JsonSerializer.Serialize(actualRows).Should().Be(JsonSerializer.Serialize(expectedRows));
    }
}
