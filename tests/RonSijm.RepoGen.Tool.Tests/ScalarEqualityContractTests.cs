using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ScalarEqualityTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScalarEqualityContractTests
{
    [Theory]
    [InlineData(false, "ALPHA")]
    [InlineData(true, "ALPHA")]
    [InlineData(false, "changed")]
    [InlineData(true, "changed")]
    [InlineData(false, null)]
    [InlineData(true, null)]
    public async Task SharedGeneratedSqliteCasesVerifyExplicitClrComparerAndConfiguredSqlTokenCollation(bool mapped, string? label)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ScalarEqualityContext(new DbContextOptionsBuilder<ScalarEqualityContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Seeds()); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var design = Builder(mapped).Build(); var assembly = SavedResultTestHarness.CompileProduction(context, design);
        var real = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ScalarEqualityRowRepositoryOperations")!, context)!;
        var (fake, data) = Fixture(mapped);
        async Task Equal<TResult>(string name, params object?[] inputs)
        {
            var expected = await Invoke<TResult>(real, name, inputs); var actual = await Invoke<TResult>(fake, name, inputs);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected)); context.ChangeTracker.Clear();
        }
        await Equal<CommandResult<ScalarEqualitySeed>>("SetLabelAsync", new ScalarEqualityRequest(1, label));
        await Equal<IReadOnlyList<int>>("ByLabelAsync", "ALPHA");
        await Equal<CommandResult<ScalarEqualitySeed>>("MixedAsync", new ScalarEqualityRequest(2, "BETA", Amount: 5));
        await Equal<CommandResult<IReadOnlyList<ScalarEqualitySeed>>>("MixedManyAsync", (object)new ScalarEqualityRequest[] { new(2, "beta", Amount: 6), new(1, label, Amount: 7) });
        await Equal<CommandResult<ScalarEqualitySeed>>("RenameAsync", new ScalarEqualityRequest(1, "fresh", "v1", "V2"));
        await Equal<CommandResult<ScalarEqualitySeed>>("RenameAsync", new ScalarEqualityRequest(1, "stale", "v1"));
        await Equal<CommandResult<IReadOnlyList<ScalarEqualitySeed>>>("RenameManyAsync", (object)new ScalarEqualityRequest[] { new(2, "second", "v1"), new(1, "first", "v2", "V3") });
        await Equal<CommandResult<int>>("DeleteAsync", new ScalarEqualityRequest(2, "", "v2"));
        await Equal<CommandResult>("StageLabelAsync", new ScalarEqualityRequest(1, "first"));
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync()); context.ChangeTracker.Clear();
        await Equal<IReadOnlyList<ScalarEqualitySeed>>("ListAsync");
    }
}
