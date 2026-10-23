using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

/// <summary>Shared behavioral cases, not a claim that LINQ-to-Objects establishes SQL/provider equivalence.</summary>
public sealed class InMemoryQueryContractTests
{
    [Fact]
    public async Task PortableContractsRunAgainstBothGeneratedFixtureAndRealSqliteRepositories()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new PortableReadContext(new DbContextOptionsBuilder<PortableReadContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(Seeds());
        await context.SaveChangesAsync();
        var model = RepositoryModelFactory.Create(context, typeof(PortableRow).Assembly, design: new PortableReadRepositoryDesigner().CreateModel());
        var productionAssembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var production = Activator.CreateInstance(productionAssembly.GetType("Generated.Portable.Persistence.PortableRowRepositoryOperations")!, context)!;
        var memory = Repository();

        async Task Equal<TResult>(string name, params object?[] inputs)
        {
            var actual = await Invoke<Task<TResult>>(memory, name, inputs);
            var expected = await Invoke<Task<TResult>>(production, name, inputs);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected), "both implementations obey the designed operation " + name);
        }
        await Equal<PortableView?>("FindAsync", 1);
        await Equal<PortableView?>("FindAsync", 99);
        await Equal<IReadOnlyList<PortableView>>("DefaultAsync");
        await Equal<int>("CountAsync");
        await Equal<bool>("AnyAsync", 31);
        await Equal<int?>("FirstValueAsync", 99);
        await Equal<int?>("SimpleValueAsync", 1);
        await Equal<int?>("AnnotatedValueAsync", 99);
        await Equal<PortableView?>("LatestAsync");
        await Equal<IReadOnlyList<PortableView>>("SearchAsync", new PortableCriteria(Term: "ap", Ids: [1, 2], SortBy: "price", Descending: true));
        await Equal<PagedResult<PortableView>>("PageAsync", 2, 2);
        await Equal<SliceResult<PortableView>>("SliceAsync", 1, 2);
        await Equal<CursorPagedResult<PortableView, int>>("CursorAsync", new CursorPageRequest<int>(2));
        await Equal<CursorPagedResult<PortableView, int>>("ResultCursorAsync", new CursorPageRequest<int>(2, 2));
        await Equal<IReadOnlyDictionary<int, string>>("DictionaryAsync");
        await Equal<IReadOnlyDictionary<string, int>>("KeepFirstAsync");
        await Equal<IReadOnlyDictionary<string, int>>("KeepLastAsync");
        await Equal<IReadOnlyDictionary<string, IReadOnlyList<string>>>("LookupAsync");
        await Equal<IReadOnlyList<PortableReport>>("ReportAsync");
        await Equal<int>("SumAsync");
        await Equal<double>("AverageAsync");
        await Equal<int>("MinAsync");
        await Equal<int>("MaxAsync");
        context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task DesignerKeySelectsThePartitionAndWritesRemainExplicitlyUnsupported()
    {
        using var context = InMemoryStoreGenerationTests.Context();
        const string consumer = """
            using System;
            using System.Threading.Tasks;
            using Generated.Memory.TestDoubles;
            using RonSijm.RepoGen.Tool.Tests;
            using RonSijm.RepoGen.Testing;
            public static class KeyedReadConsumer
            {
                public static async Task<bool> Exercise()
                {
                    var fixture = new MemoryFixtureContextRepositoryFixture();
                    var first = new MemoryContextKey("one");
                    var second = new MemoryContextKey("two");
                    fixture.For(first).MemoryFixtureRowStore.Seed(new() { Id = 1, Name = "first" });
                    fixture.For(second).MemoryFixtureRowStore.Seed(new() { Id = 1, Name = "second" });
                    var repository = new MemoryFixtureRowRepositoryTestDouble(fixture);
                    if ((await repository.FindAsync(new(first, 1)))?.Name != "first") return false;
                    if ((await repository.FindAsync(new(second, 1)))?.Name != "second") return false;
                    try { await repository.DeleteAsync(new(first, 1)); return false; }
                    catch (UnsupportedInMemoryRepositoryOperationException failure)
                    {
                        if (!failure.Operation.Contains("DeleteAsync", StringComparison.Ordinal)) return false;
                    }
                    repository.OnDeleteAsync.Returns(1);
                    if (await repository.DeleteAsync(new(first, 1)) != 1 || fixture.For(first).MemoryFixtureRowStore.Count != 1) return false;
                    repository.ResetTestDouble();
                    return (await repository.FindAsync(new(second, 1)))?.Name == "second";
                }
            }
            """;
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new MemoryFixtureRepositoryDesigner(keyed: true, portable: true)), consumer);
        (await (Task<bool>)assembly.GetType("KeyedReadConsumer")!.GetMethod("Exercise")!.Invoke(null, null)!).Should().BeTrue();
    }

    [Fact]
    public async Task UnsupportedQueryCanBeScriptedButCannotMasqueradeAsAnEmptyResult()
    {
        using var context = Context();
        var builder = Builder(strict: true);
        Entity(builder).HasQuery("SqlCount").Where(row => EF.Property<int>(row, "Id") > 0)
            .RequireTestOverride("Provider test owns SQL semantics.").Count();
        const string consumer = """
            using System;
            using System.Threading.Tasks;
            using Generated.Portable.TestDoubles;
            using RonSijm.RepoGen.Testing;
            public static class UnsupportedReadConsumer
            {
                public static async Task<bool> Exercise()
                {
                    var repository = new PortableRowRepositoryTestDouble(new PortableReadContextRepositoryFixture());
                    try { await repository.SqlCountAsync(); return false; }
                    catch (UnsupportedInMemoryRepositoryOperationException failure)
                    {
                        if (!failure.Reason.Contains("EF.Property", StringComparison.Ordinal)) return false;
                    }
                    repository.OnSqlCountAsync.EnqueueResult(42);
                    if (await repository.SqlCountAsync() != 42) return false;
                    try { await repository.SqlCountAsync(); return false; }
                    catch (UnsupportedInMemoryRepositoryOperationException) { }
                    repository.OnSqlCountAsync.Returns(3);
                    repository.ResetTestDouble();
                    try { await repository.SqlCountAsync(); return false; }
                    catch (UnsupportedInMemoryRepositoryOperationException) { return true; }
                }
            }
            """;
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()), consumer);
        (await (Task<bool>)assembly.GetType("UnsupportedReadConsumer")!.GetMethod("Exercise")!.Invoke(null, null)!).Should().BeTrue();
    }
}
