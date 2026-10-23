using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class TransactionRunnerTests
{
    [Fact]
    public async Task TransientAttemptsUseFreshOwnedTrackingStateAndNotifyOnlyAfterTheWinningCommit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var provider = new Provider(connection);
        var observer = new Recorder();
        await using (var setup = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Owners.Add(new CommandOwner { Id = 1 });
            await setup.SaveChangesAsync();
        }

        var attempts = 0;
        var result = await RepositoryTransactionRunner.ExecuteAsync(provider, async (work, token) =>
        {
            await using var lease = await work.AcquireAsync(token);
            lease.Context.Rows.Add(new CommandRow { Name = "attempt " + ++attempts, OwnerId = 1 });
            if (attempts == 1)
            {
                throw new FixtureTransientException();
            }

            return "success";
        }, [observer]);
        result.Should().Be("success");
        attempts.Should().Be(2);
        observer.Calls.Should().Be(1);
        provider.Contexts.Should().HaveCount(3).And.OnlyContain(context => context.DisposeCount == 1);
        await using var verify = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
        (await verify.Rows.Select(row => row.Name).ToListAsync()).Should().Equal("attempt 2");
    }

    [Fact]
    public async Task BorrowedContextsAreNotReplayedAndAfterCommitObserverFailureIsNotRetried()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Owners.Add(new CommandOwner { Id = 1 });
        await context.SaveChangesAsync();
        Func<Task> borrowed = async () => await RepositoryTransactionRunner.ExecuteAsync(new ScopedDbContextLeaseProvider<CommandContext>(context), (_, _) => Task.FromResult(1));
        await borrowed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*owned contexts*");
        var provider = new Provider(connection);
        var observer = new Recorder { Fail = true };
        var attempts = 0;
        Func<Task> committed = async () => await RepositoryTransactionRunner.ExecuteAsync(provider, async (work, token) =>
        {
            await using var lease = await work.AcquireAsync(token);
            lease.Context.Rows.Add(new CommandRow { Name = "committed", OwnerId = 1 });
            attempts++;
            return 1;
        }, [observer]);
        await committed.Should().ThrowAsync<RepositoryCommitObserverException>();
        attempts.Should().Be(1);
        observer.Calls.Should().Be(1);
        (await context.Rows.CountAsync()).Should().Be(1);
    }

    [Fact]
    public void TimeoutScopesRestoreExistingAndUnspecifiedSettings()
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        context.Database.GetCommandTimeout().Should().BeNull();
        using (new RepositoryCommandTimeoutScope(context, 5))
        {
            context.Database.GetCommandTimeout().Should().Be(5);
        }

        context.Database.GetCommandTimeout().Should().BeNull();
        context.Database.SetCommandTimeout(7);
        using (new RepositoryCommandTimeoutScope(context, 2))
        {
            context.Database.GetCommandTimeout().Should().Be(2);
        }

        context.Database.GetCommandTimeout().Should().Be(7);
    }

    private sealed class Provider(SqliteConnection connection) : IDbContextLeaseProvider<CommandContext>
    {
        public List<CommandContext> Contexts { get; } = [];

        public ValueTask<DbContextLease<CommandContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).ReplaceService<IExecutionStrategyFactory, FixtureStrategyFactory>().Options);
            Contexts.Add(context);
            return ValueTask.FromResult(DbContextLease.Owned(context, "fixture"));
        }
    }

    private sealed class Recorder : IRepositoryCommitObserver
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }

        public Task CommittedAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fail ? Task.FromException(new FixtureTransientException()) : Task.CompletedTask;
        }
    }
}

public sealed class FixtureTransientException : Exception;

public sealed class FixtureStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
{
    public IExecutionStrategy Create() => new FixtureRetryStrategy(dependencies);
}

public sealed class FixtureRetryStrategy(ExecutionStrategyDependencies dependencies) : ExecutionStrategy(dependencies, 2, TimeSpan.Zero)
{
    protected override bool ShouldRetryOn(Exception exception) => exception is FixtureTransientException;
}