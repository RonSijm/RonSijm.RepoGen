using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.Sqlite;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AtomicWriteTests
{
    private static readonly string[] Contenders = ["A", "B"];

    [Fact]
    public async Task CompetingInsertsAndConditionalUpsertsHaveOneWinnerAndReturnGeneratedValues()
    {
        var file = Path.Combine(Path.GetTempPath(), "repogen-atomic-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var options = new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=" + file + ";Pooling=False;Default Timeout=10").Options;
            System.Reflection.Assembly assembly;
            await using (var context = new CommandContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                context.Owners.AddRange(new CommandOwner { Id = 1 }, new CommandOwner { Id = 2 });
                await context.SaveChangesAsync();
                assembly = ClosedRepositoryTests.Compile(Model(context));
            }

            var inserts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            {
                await using var context = new CommandContext(options);
                return await Call<Task<CommandResult<CommandDto>>>(Operations(assembly, new ScopedDbContextLeaseProvider<CommandContext>(context)), "EnsureAsync", new AtomicRequest("Shared", "original", 1, 0, 1), CancellationToken.None);
            })));
            inserts.Count(static result => result.Status == CommandStatus.Inserted).Should().Be(1);
            inserts.Count(static result => result.Status == CommandStatus.AlreadyExists).Should().Be(7);
            var created = inserts.Single(static result => result.Status == CommandStatus.Inserted).Value!;
            created.Id.Should().BeGreaterThan(0);
            created.Normalized.Should().Be("SHARED");
            var updates = await Task.WhenAll(Contenders.Select(description => Task.Run(async () =>
            {
                await using var context = new CommandContext(options);
                return await Call<Task<CommandResult<CommandDto>>>(Operations(assembly, new ScopedDbContextLeaseProvider<CommandContext>(context)), "UpsertAsync", new AtomicRequest("Shared", description, 1, 1, 2), CancellationToken.None);
            })));
            updates.Count(static result => result.Status == CommandStatus.Updated).Should().Be(1);
            updates.Count(static result => result.Status == CommandStatus.NotMatched).Should().Be(1);
            await using var verify = new CommandContext(options);
            var row = await verify.Rows.SingleAsync();
            row.Revision.Should().Be(2);
            row.Id.Should().Be(created.Id);
            var wrongOwner = await Call<Task<CommandResult<CommandDto>>>(Operations(assembly, new ScopedDbContextLeaseProvider<CommandContext>(verify)), "UpsertAsync", new AtomicRequest("Shared", "illegal", 2, 2, 3), CancellationToken.None);
            wrongOwner.Status.Should().Be(CommandStatus.NotMatched);
            (await verify.Rows.AsNoTracking().SingleAsync()).OwnerId.Should().Be(1);
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public async Task ATrackedDeleteAndReturnCannotConsumeTheSameRowTwice()
    {
        var file = Path.Combine(Path.GetTempPath(), "repogen-consume-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var options = new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=" + file + ";Pooling=False;Default Timeout=10").Options;
            System.Reflection.Assembly assembly;
            await using (var context = new CommandContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                context.Owners.Add(new CommandOwner { Id = 1 });
                context.Rows.Add(new CommandRow { Name = "consume", OwnerId = 1, Revision = 1 });
                await context.SaveChangesAsync();
                assembly = ClosedRepositoryTests.Compile(Model(context));
            }

            var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            {
                await using var context = new CommandContext(options);
                return await Call<Task<CommandResult<CommandDto>>>(Operations(assembly, new ScopedDbContextLeaseProvider<CommandContext>(context)), "ConsumeAsync",
                    new AtomicRequest("consume", null, 1, 1, 2), CancellationToken.None);
            })));
            outcomes.Count(static outcome => outcome.Status == CommandStatus.Applied).Should().Be(1);
            outcomes.Count(static outcome => outcome.HasValue).Should().Be(1);
            outcomes.Where(static outcome => !outcome.Succeeded).Should().OnlyContain(static outcome => outcome.Status == CommandStatus.NotFound || outcome.Status == CommandStatus.Conflict);
            await using var verify = new CommandContext(options);
            (await verify.Rows.CountAsync()).Should().Be(0);
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public async Task ProjectionFailureRollsBackTheOperationAndExplicitTransactionsDeferObservers()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Owners.Add(new CommandOwner { Id = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var assembly = ClosedRepositoryTests.Compile(Model(context));
        var observer = new Recorder();
        await using var work = new RepositoryUnitOfWork<CommandContext>(context, [observer]);
        var operations = Operations(assembly, work);
        Func<Task> fail = async () => await Call<Task<CommandResult<CommandDto>>>(operations, "FailAsync", new AtomicRequest("failure", null, 1, 0, 1), CancellationToken.None);
        await fail.Should().ThrowAsync<InvalidOperationException>().WithMessage("*post-save projection failure*");
        (await context.Rows.CountAsync()).Should().Be(0);
        observer.Calls.Should().Be(0);
        await using var transaction = await work.BeginTransactionAsync();
        (await Call<Task<CommandResult<CommandDto>>>(operations, "EnsureAsync", new AtomicRequest("rollback", null, 1, 0, 1), CancellationToken.None)).Status.Should().Be(CommandStatus.Inserted);
        observer.Calls.Should().Be(0);
        await transaction.RollbackAsync();
        (await context.Rows.CountAsync()).Should().Be(0);
        observer.Calls.Should().Be(0);
        context.ChangeTracker.Entries<CommandRow>().Should().BeEmpty();
    }

    [Theory]
    [InlineData("missingWriter")]
    [InlineData("notUnique")]
    [InlineData("changeKey")]
    [InlineData("stage")]
    public void UnsupportedAtomicConfigurationsAreDiagnosed(string scenario)
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Atomic.Contracts").GenerateOnlyConfiguredEntities());
        if (scenario != "missingWriter")
        {
            builder.Configure(options => options.UseAtomicWrites<SqliteAtomicWriter>());
        }

        var command = builder.Entity<CommandRow>().HasCommand("Invalid").Create<AtomicRequest>(request => new CommandRow { Name = request.Name, Description = request.Description, OwnerId = request.OwnerId, Revision = request.NewVersion })
            .AllowWrite(row => row.OwnerId).AllowWrite(row => row.Revision).InsertIfAbsent(row => row.Name);
        if (scenario == "notUnique")
        {
            command.InsertIfAbsent(row => row.Description);
        }

        if (scenario == "changeKey")
        {
            command.InsertOrUpdate(row => row.Name, update => update.SetProperty<string, AtomicRequest>(row => row.Name, (row, request) => request.Description!));
        }

        if (scenario == "stage")
        {
            command.Stage();
        }

        Action generate = () => RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>();
    }

    private static RepositoryGenerationModel Model(CommandContext context)
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Atomic.Contracts").UseAsyncContextLeases().UseAtomicWrites<SqliteAtomicWriter>().GenerateOnlyConfiguredEntities());
        builder.Entity<CommandRow>(entity =>
        {
            entity.HasCommand("Ensure").Create<AtomicRequest>(request => new CommandRow { Name = request.Name, Description = request.Description, OwnerId = request.OwnerId, Revision = request.NewVersion })
                .AllowWrite(row => row.OwnerId).AllowWrite(row => row.Revision).InsertIfAbsent(row => row.Name)
                .Returns(row => new CommandDto(row.Id, row.Name, row.Description, row.Revision, row.Normalized));
            entity.HasCommand("Upsert").Create<AtomicRequest>(request => new CommandRow { Name = request.Name, Description = request.Description, OwnerId = request.OwnerId, Revision = request.NewVersion })
                .AllowWrite(row => row.OwnerId).AllowWrite(row => row.Revision).Where<AtomicRequest>((row, request) => row.OwnerId == request.OwnerId)
                .InsertOrUpdate(row => row.Name, update => update.SetProperty<string?, AtomicRequest>(row => row.Description, (row, request) => request.Description)
                    .SetProperty<int, AtomicRequest>(row => row.Revision, (row, request) => request.NewVersion))
                .ExpectVersion<int, AtomicRequest>(row => row.Revision, (row, request) => request.ExpectedVersion)
                .Returns(row => new CommandDto(row.Id, row.Name, row.Description, row.Revision, row.Normalized));
            entity.HasCommand("Fail").Create<AtomicRequest>(request => new CommandRow { Name = request.Name, Description = request.Description, OwnerId = request.OwnerId, Revision = request.NewVersion })
                .AllowWrite(row => row.OwnerId).AllowWrite(row => row.Revision).InsertIfAbsent(row => row.Name).Returns(row => CommandMapping.Fail(row));
            entity.HasCommand("Consume").Where<AtomicRequest>((row, request) => row.Name == request.Name && row.OwnerId == request.OwnerId)
                .TrackedDelete().ExpectVersion<int, AtomicRequest>(row => row.Revision, (row, request) => request.ExpectedVersion).Save()
                .Returns(row => new CommandDto(row.Id, row.Name, row.Description, row.Revision, row.Normalized));
        });
        return RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build());
    }

    private static object Operations(System.Reflection.Assembly assembly, IDbContextLeaseProvider<CommandContext> source) =>
        Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, source, new SqliteAtomicWriter())!;

    private static T Call<T>(object instance, string name, params object[] arguments) => (T)instance.GetType().GetMethod(name)!.Invoke(instance, arguments)!;

    private sealed class Recorder : IRepositoryCommitObserver
    {
        public int Calls { get; private set; }

        public Task CommittedAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}

public sealed record AtomicRequest(string Name, string? Description, int OwnerId, int ExpectedVersion, int NewVersion);