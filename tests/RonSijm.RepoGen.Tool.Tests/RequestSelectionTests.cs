using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class RequestSelectionTests
{
    [Fact]
    public async Task KeySelectionBindingAndValidationUseOneRequestAndAWorkCannotSwitchDatabases()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Owners.Add(new CommandOwner { Id = 1 });
        context.Rows.Add(new CommandRow { Name = "initial", OwnerId = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Request.Contracts").UseKeyedContextLeases<string>().GenerateOnlyConfiguredEntities());
        builder.Entity<CommandRow>(entity =>
        {
            var owner = entity.HasFragment<KeyedRequest>("Owned", (row, request) => row.OwnerId == request.OwnerId);
            entity.HasQuery("Names").UseContextKey<KeyedRequest, string>(request => request.Database)
                .Validate<KeyedRequest>(request => request.OwnerId > 0, "An owner is required.").Use(owner).Select(row => row.Name).List();
            entity.HasCommand("Rename").UseContextKey<KeyedRequest, string>(request => request.Database).Use(owner)
                .Update(update => update.SetProperty<string, KeyedRequest>(row => row.Name, (row, request) => request.Name));
            entity.HasQuery("Report").UseContextKey<KeyedRequest, string>(request => request.Database).Use(owner)
                .GroupBy<int, RequestAggregate, KeyedRequest>(row => row.OwnerId, (group, request) => new RequestAggregate(group.Key, group.Count(row => row.Name == request.Name)));
        });
        var model = RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build());
        var generated = ClosedRepositoryTests.Compile(model);
        var provider = new Provider(context);
        await using var work = new KeyedRepositoryUnitOfWork<CommandContext, string>(provider);
        var operations = Activator.CreateInstance(generated.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, work)!;
        Func<Task> invalid = async () => await Read(operations, new("A", 0, ""));
        await invalid.Should().ThrowAsync<ArgumentException>().WithMessage("*owner*");
        provider.Calls.Should().Be(0);
        (await Read(operations, new("A", 1, ""))).Should().Equal("initial");
        await Call<Task<int>>(operations, "RenameAsync", new KeyedRequest("A", 1, "changed"), CancellationToken.None);
        (await Read(operations, new("A", 1, ""))).Should().Equal("changed");
        (await Call<Task<IReadOnlyList<RequestAggregate>>>(operations, "ReportAsync", new KeyedRequest("A", 1, "changed"), CancellationToken.None)).Should().Equal(new RequestAggregate(1, 1));
        Func<Task> switchKey = async () => await Read(operations, new("B", 1, ""));
        await switchKey.Should().ThrowAsync<InvalidOperationException>().WithMessage("*switch context keys*");
        provider.Keys.Should().Equal("A");
        provider.Calls.Should().Be(1);
        (await work.AcquireAsync("A")).ScopeKey.Should().Be("database:A");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingAndMismatchedKeyBindingsFailBeforeGeneration(bool mismatched)
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Request.Contracts").UseKeyedContextLeases<string>().GenerateOnlyConfiguredEntities());
        var query = builder.Entity<CommandRow>().HasQuery("Read").Count();
        if (mismatched)
        {
            query.UseContextKey<KeyedRequest, int>(request => request.OwnerId);
        }

        Action generate = () => RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*must declare UseContextKey*");
    }

    private static Task<IReadOnlyList<string>> Read(object instance, KeyedRequest request) => Call<Task<IReadOnlyList<string>>>(instance, "NamesAsync", request, CancellationToken.None);
    private static T Call<T>(object instance, string name, params object[] arguments) => (T)instance.GetType().GetMethod(name)!.Invoke(instance, arguments)!;

    private sealed class Provider(CommandContext context) : IKeyedDbContextLeaseProvider<CommandContext, string>
    {
        public int Calls { get; private set; }
        public List<string> Keys { get; } = [];

        public ValueTask<DbContextLease<CommandContext>> AcquireAsync(string key, CancellationToken cancellationToken = default)
        {
            Calls++;
            Keys.Add(key);
            return ValueTask.FromResult(DbContextLease.Borrowed(context, "database:" + key));
        }
    }
}

public sealed record KeyedRequest(string Database, int OwnerId, string Name);

public sealed record RequestAggregate(int OwnerId, int Count);