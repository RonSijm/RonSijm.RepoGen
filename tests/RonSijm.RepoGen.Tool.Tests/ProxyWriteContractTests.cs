using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ProxyWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

[Collection("Proxy writes")]
public sealed class ProxyWriteContractTests
{
    [Fact]
    public async Task PublicRowAdaptersShareNamedCommandContractsWithSqliteInternalModels()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new ProxyWriteContext(new DbContextOptionsBuilder<ProxyWriteContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new ProxyWriteRecord { Id = 7, Name = "old", Stamp = [1, 2] }, new ProxyWriteRecord { Id = 8, Name = "hidden", Hidden = true, Stamp = [3] });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var production = CompileProduction(context);
        var real = Activator.CreateInstance(production.GetType("Generated.ProxyWrites.Persistence.ProxyWriteRecordRepositoryOperations")!, context)!;
        var realAudit = Activator.CreateInstance(production.GetType("Generated.ProxyWrites.Persistence.ProxyWriteAuditRepositoryOperations")!, context)!;
        var (memory, audit, data, store) = Fixture();
        async Task Equal<TResult>(string name, params object?[] inputs)
        {
            var expected = await Invoke<TResult>(real, name, inputs); var actual = await Invoke<TResult>(memory, name, inputs);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected), name);
        }
        await Equal<IReadOnlyList<ProxyWriteView>>("ListAsync");
        await Equal<CommandResult<ProxyWriteView>>("CreateAsync", new ProxyWriteRequest(0, "first"));
        await Equal<CommandResult>("StageCreateAsync", new ProxyWriteRequest(0, "reserved"));
        await Equal<CommandResult>("StageRenameAsync", new ProxyWriteRequest(7, "saved", Stamp: [4, 5]));
        var input = new ProxyWriteRequest(1, "audit");
        (await Invoke<CommandResult>(audit, "StageAuditAsync", input)).Should().BeEquivalentTo(await Invoke<CommandResult>(realAudit, "StageAuditAsync", input));
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync());
        await Equal<IReadOnlyList<ProxyWriteView>>("ListAsync");
        await Equal<CommandResult<ProxyWriteView>>("RenameAsync", new ProxyWriteRequest(7, "stale", 3));
        await Equal<CommandResult<ProxyWriteView>>("RenameAsync", new ProxyWriteRequest(7, "current", 5));
        await Equal<CommandResult<ProxyWriteView>>("DeleteAsync", new ProxyWriteRequest(8, "hidden", 6));
        await Equal<CommandResult<IReadOnlyList<ProxyWriteView>>>("RenameManyAsync", (IReadOnlyList<ProxyWriteRequest>)[new(10, "A", 8), new(9, "BC", 5)]);
        await Equal<CommandResult<IReadOnlyList<ProxyWriteView>>>("DeleteManyAsync", (IReadOnlyList<ProxyWriteRequest>)[new(9, "", 2), new(10, "", 1)]);
        await Equal<CommandResult<ProxyWriteView>>("CreateAsync", new ProxyWriteRequest(40, "explicit", Label: "supplied", Stamp: [7]));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal<CommandResult<ProxyWriteView>>("DeleteAsync", new ProxyWriteRequest(7, "", 7));
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear(); await Equal<IReadOnlyList<ProxyWriteView>>("ListAsync");
        (await context.Set<ProxyWriteRecord>().CountAsync()).Should().Be(store.Count);
    }
}
