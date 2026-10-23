using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class RegisteredQueryTests
{
    [Fact]
    public async Task RegisteredParameterizedSqlComposesResultsAndUsesTheSharedTransaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Owners.Add(new CommandOwner { Id = 1 });
        context.Rows.Add(new CommandRow { Id = 1, Name = "Ada", OwnerId = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Sql.Contracts").UseAsyncContextLeases().GenerateOnlyConfiguredEntities());
        builder.Entity<CommandRow>(entity =>
        {
            entity.HasQuery("Find").FromProvider<SqlNamesQuery, SqlNameRequest, SqlNameRow>(request => request).OrderBy(row => row.Id).List();
            entity.HasQuery("Page").FromProvider<SqlNamesQuery, SqlNameRequest, SqlNameRow>(request => request).OrderBy(row => row.Id).Paged();
            entity.HasQuery("Dictionary").FromProvider<SqlNamesQuery, SqlNameRequest, SqlNameRow>(request => request).Dictionary(row => row.Id, row => row.Name);
            entity.HasQuery("Stream").FromProvider<SqlNamesQuery, SqlNameRequest, SqlNameRow>(request => request).OrderBy(row => row.Id).Stream();
        });
        var model = RepositoryModelFactory.Create(context, typeof(CommandContext).Assembly, true, builder.Build());
        var assembly = ClosedRepositoryTests.Compile(model);
        var root = new SqlNamesQuery();
        await using var work = new RepositoryUnitOfWork<CommandContext>(context);
        var operations = Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, work, root, root, root, root)!;
        (await Call<Task<IReadOnlyList<SqlNameRow>>>(operations, "FindAsync", new SqlNameRequest("Ada"), CancellationToken.None)).Should().ContainSingle().Which.Name.Should().Be("Ada");
        (await Call<Task<IReadOnlyList<SqlNameRow>>>(operations, "FindAsync", new SqlNameRequest("'; DELETE FROM Rows; --"), CancellationToken.None)).Should().BeEmpty();
        (await context.Rows.CountAsync()).Should().Be(1);
        (await Call<Task<PagedResult<SqlNameRow>>>(operations, "PageAsync", new SqlNameRequest("Ada"), 1, 10, CancellationToken.None)).TotalCount.Should().Be(1);
        (await Call<Task<IReadOnlyDictionary<int, string>>>(operations, "DictionaryAsync", new SqlNameRequest("Ada"), CancellationToken.None))[1].Should().Be("Ada");
        await using (var transaction = await work.BeginTransactionAsync())
        {
            await context.Rows.ExecuteUpdateAsync(update => update.SetProperty(row => row.Name, "Grace"));
            (await Call<Task<IReadOnlyList<SqlNameRow>>>(operations, "FindAsync", new SqlNameRequest("Grace"), CancellationToken.None)).Should().ContainSingle();
            root.SawTransaction.Should().BeTrue();
            await transaction.RollbackAsync();
        }

        var provider = new ScopedDbContextLeaseProvider<CommandContext>(context);
        operations = Activator.CreateInstance(operations.GetType(), provider, root, root, root, root)!;
        var stream = Call<IAsyncEnumerable<SqlNameRow>>(operations, "StreamAsync", new SqlNameRequest("Ada"), CancellationToken.None);
        await using var enumerator = stream.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Func<Task> cancel = async () => await Call<Task<IReadOnlyList<SqlNameRow>>>(operations, "FindAsync", new SqlNameRequest("Ada"), canceled.Token);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        ClosedRepositorySourceRenderer.RenderContracts(model).Should().NotContain("QueryRoot").And.NotContain("IQueryable").And.NotContain("DbContext");
    }

    [Fact]
    public async Task CachedRegisteredPagesUseTheCacheFactoryCancellationTokenAndKeepResultOrdering()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Owners.Add(new CommandOwner { Id = 1 });
        context.Rows.Add(new CommandRow { Name = "Ada", OwnerId = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Sql.Contracts").UseClosedCaching("sql-fixture").GenerateOnlyConfiguredEntities());
        builder.Entity<CommandRow>().HasQuery("Page").Cache()
            .FromProvider<SqlNamesQuery, SqlNameRequest, SqlNameRow>(request => request)
            .OrderBy(row => row.Name).ThenBy(row => row.Id).RequirePagingOrder().Paged(RepositoryPageConsistency.Serializable);
        var assembly = ClosedRepositoryTests.Compile(RepositoryModelFactory.Create(context, typeof(CommandContext).Assembly, true, builder.Build()));
        var cache = new FactoryTokenCache();
        var operations = Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, context, cache, new SqlNamesQuery())!;
        (await Call<Task<PagedResult<SqlNameRow>>>(operations, "PageAsync", new SqlNameRequest("Ada"), 1, 10, CancellationToken.None)).TotalCount.Should().Be(1);
        context.Database.CurrentTransaction.Should().BeNull();
        cache.CancelFactory = true;
        Func<Task> canceled = async () => await Call<Task<PagedResult<SqlNameRow>>>(operations, "PageAsync", new SqlNameRequest("Ada"), 1, 10, CancellationToken.None);
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        context.Database.CurrentTransaction.Should().BeNull();
    }

    [Fact]
    public async Task RegisteredOverloadsShareAnIdenticalProviderBindingWithoutDuplicateInfrastructureFields()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Owners.Add(new CommandOwner { Id = 1 });
        context.Rows.Add(new CommandRow { Id = 1, Name = "Ada", OwnerId = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Sql.Contracts").GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<CommandRow>();
        entity.HasQuery("Find").FromProvider<SqlNamesQuery, SqlNameRequest, SqlNameRow>(request => request).List();
        entity.HasQuery("Find").FromProvider<SqlNamesQuery, SqlNameRequest, SqlNameRow>(request => request)
            .Where<int>((row, minimum) => row.Id >= minimum).List();
        var assembly = ClosedRepositoryTests.Compile(RepositoryModelFactory.Create(context, typeof(CommandContext).Assembly, true, builder.Build()));
        var operations = Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, context, new SqlNamesQuery())!;
        var method = operations.GetType().GetMethod("FindAsync", [typeof(SqlNameRequest), typeof(int), typeof(CancellationToken)])!;
        (await (Task<IReadOnlyList<SqlNameRow>>)method.Invoke(operations, [new SqlNameRequest("Ada"), 2, CancellationToken.None])!).Should().BeEmpty();
    }

    [Fact]
    public void IncorrectProviderCapabilitiesAreDiagnosed()
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Sql.Contracts").GenerateOnlyConfiguredEntities());
        builder.Entity<CommandRow>().HasQuery("Invalid").FromProvider<string, SqlNameRequest, SqlNameRow>(request => request).List();
        Action generate = () => RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*IRepositoryQueryRoot*");
    }

    private static T Call<T>(object instance, string name, params object[] arguments) => (T)instance.GetType().GetMethod(name)!.Invoke(instance, arguments)!;

    private sealed class FactoryTokenCache : IRepositoryResultCache
    {
        public bool CancelFactory { get; set; }

        public Task<T> GetOrCreateAsync<T>(string key, IReadOnlyCollection<string> tags, RepositoryCacheOptions options, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default) =>
            factory(CancelFactory ? new CancellationToken(canceled: true) : cancellationToken);

        public Task InvalidateAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

public sealed record SqlNameRequest(string Name);

public sealed class SqlNameRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class SqlNamesQuery : IRepositoryQueryRoot<CommandContext, SqlNameRequest, SqlNameRow>
{
    public bool SawTransaction { get; private set; }

    public IQueryable<SqlNameRow> CreateQuery(CommandContext context, SqlNameRequest request)
    {
        SawTransaction |= context.Database.CurrentTransaction is not null;
        return context.Database.SqlQuery<SqlNameRow>($"SELECT Id, Name FROM Rows WHERE Name = {request.Name}");
    }
}