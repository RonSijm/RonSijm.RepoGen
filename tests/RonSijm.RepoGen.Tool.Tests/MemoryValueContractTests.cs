using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryValueContractTests
{
    [Fact]
    public async Task GeneratedFactoriesShareNamedContractsWithActualSqliteDefaultsAndComputedValues()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new MemoryValueContext(new DbContextOptionsBuilder<MemoryValueContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.Add(new MemoryValueRow { Id = 7, Title = "old" }); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var realAssembly = MemoryValueTestHarness.CompileProduction(context);
        var real = Activator.CreateInstance(realAssembly.GetType("Generated.MemoryValues.Persistence.MemoryValueRowRepositoryOperations")!, context)!;
        var (memory, data, store) = MemoryValueTestHarness.Fixture(ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new MemoryValueRepositoryDesigner())));
        async Task Equal<TResult>(string name, object? input = null)
        {
            var inputs = input is null ? Array.Empty<object?>() : [input];
            var expected = await Invoke<TResult>(real, name, inputs); var actual = await Invoke<TResult>(memory, name, inputs);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected), name);
        }
        await Equal<CommandResult<MemoryValueView>>("CreateAsync", new MemoryValueRequest(0, "eight"));
        await Equal<CommandResult>("StageCreateAsync", new MemoryValueRequest(0, "six"));
        await Equal<IReadOnlyList<MemoryValueView>>("ListAsync");
        await Equal<CommandResult<MemoryValueView>>("CreateAsync", new MemoryValueRequest(0, "seven"));
        await Equal<CommandResult>("StageRenameAsync", new MemoryValueRequest(7, "two"));
        await Equal<CommandResult<MemoryValueView>>("RenameAsync", new MemoryValueRequest(7, "changed"));
        await Equal<CommandResult<MemoryValueView>>("RenameAsync", new MemoryValueRequest(7, "stale"));
        await Equal<CommandResult<IReadOnlyList<MemoryValueView>>>("RenameManyAsync", new MemoryValueRequest[] { new(10, "XX", 5), new(8, "Z", 5) });
        await using (var transaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal<CommandResult<MemoryValueView>>("RenameAsync", new MemoryValueRequest(7, "rollback", 7));
            await Equal<CommandResult<MemoryValueView>>("DeleteAsync", new MemoryValueRequest(9, "", 3));
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear(); await Equal<IReadOnlyList<MemoryValueView>>("ListAsync");
        await Equal<CommandResult<MemoryValueView>>("CreateAsync", new MemoryValueRequest(40, "explicit", Label: "provided"));
        (await context.Set<MemoryValueRow>().CountAsync()).Should().Be(store.Count);
    }
}
