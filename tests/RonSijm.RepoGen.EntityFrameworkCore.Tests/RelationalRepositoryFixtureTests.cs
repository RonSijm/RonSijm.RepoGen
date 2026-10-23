using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Testing.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.EntityFrameworkCore.Tests;

public sealed class RelationalRepositoryFixtureTests
{
    [Fact]
    public async Task FixtureInitializesOnceAndResolvesRepositoriesAndContextInFreshOwnedScopes()
    {
        var initialized = 0;
        var seeded = 0;
        await using var fixture = await RelationalRepositoryFixture.CreateAsync<RelationalFixtureContext>(options =>
        {
            options.ConfigureServices = Register;
            options.InitializeDatabaseAsync = async (context, token) => { initialized++; await context.Database.EnsureCreatedAsync(token); };
            options.SeedAsync = async (context, token) =>
            {
                seeded++;
                context.Add(new RelationalFixtureRow { Name = "seed" });
                await context.SaveChangesAsync(token);
            };
        });
        initialized.Should().Be(1);
        seeded.Should().Be(1);
        fixture.ActiveSessions.Should().Be(0);
        await using var first = await fixture.OpenSessionAsync();
        await using var second = await fixture.OpenSessionAsync();
        first.Context.Should().NotBeSameAs(second.Context);
        first.GetRepository<RelationalFixtureRepository>().Context.Should().BeSameAs(first.Context);
        fixture.ActiveSessions.Should().Be(2);
        first.Context.Add(new RelationalFixtureRow { Name = "not saved" });
        (await second.Context.Set<RelationalFixtureRow>().CountAsync()).Should().Be(1);
        initialized.Should().Be(1);
        seeded.Should().Be(1);
    }

    [Fact]
    public async Task DefaultCallbacksDoNotCreateDeleteOrResetDatabaseState()
    {
        await using var fixture = await RelationalRepositoryFixture.CreateAsync<RelationalFixtureContext>(options => options.ConfigureServices = Register);
        await using var session = await fixture.OpenSessionAsync();
        Func<Task> query = () => session.Context.Set<RelationalFixtureRow>().CountAsync();
        await query.Should().ThrowAsync<SqliteException>().WithMessage("*no such table*");
        Func<Task> reset = () => fixture.ResetAsync();
        await reset.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No ResetDatabaseAsync*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitRegistrationIsRequiredAndSingletonContextsAreRejected(bool singleton)
    {
        Func<Task> create = () => RelationalRepositoryFixture.CreateAsync<RelationalFixtureContext>(options =>
        {
            if (singleton) options.ConfigureServices = services => services.AddSingleton(_ => new RelationalFixtureContext(new DbContextOptionsBuilder<RelationalFixtureContext>().UseSqlite("Data Source=:memory:").Options));
        });
        await create.Should().ThrowAsync<InvalidOperationException>().WithMessage(singleton ? "*never singleton*" : "*explicit ConfigureServices*");
    }

    [Fact]
    public async Task TransientContextsAreRejectedSoPortsInOneSessionDoNotResolveDifferentContexts()
    {
        Func<Task> create = () => RelationalRepositoryFixture.CreateAsync<RelationalFixtureContext>(options =>
            options.ConfigureServices = services => services.AddDbContext<RelationalFixtureContext>(
                builder => builder.UseSqlite("Data Source=:memory:"), contextLifetime: ServiceLifetime.Transient));
        await create.Should().ThrowAsync<InvalidOperationException>().WithMessage("*never singleton or transient*");
    }

    [Fact]
    public async Task EfInMemoryIsRejectedRatherThanCalledRelationalVerification()
    {
        Func<Task> create = () => RelationalRepositoryFixture.CreateAsync<RelationalFixtureContext>(options =>
            options.ConfigureServices = services => services.AddDbContext<RelationalFixtureContext>(builder => builder.UseInMemoryDatabase("fixture-provider-guard")));
        await create.Should().ThrowAsync<InvalidOperationException>().WithMessage("*real relational provider*");
    }

    [Fact]
    public async Task ExplicitResetRunsInFreshScopeAndReseedsButRefusesOpenSessions()
    {
        var seeds = 0;
        await using var fixture = await RelationalRepositoryFixture.CreateAsync<RelationalFixtureContext>(options =>
        {
            options.ConfigureServices = Register;
            options.InitializeDatabaseAsync = (context, token) => context.Database.EnsureCreatedAsync(token);
            options.ResetDatabaseAsync = (context, token) => context.Set<RelationalFixtureRow>().ExecuteDeleteAsync(token);
            options.SeedAsync = async (context, token) =>
            {
                context.Add(new RelationalFixtureRow { Name = "seed" + ++seeds });
                await context.SaveChangesAsync(token);
            };
        });
        var session = await fixture.OpenSessionAsync();
        Func<Task> reset = () => fixture.ResetAsync();
        await reset.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Close all fixture sessions*");
        seeds.Should().Be(1);
        await session.DisposeAsync();
        await fixture.ResetAsync();
        fixture.ActiveSessions.Should().Be(0);
        await using var inspection = await fixture.OpenSessionAsync();
        (await inspection.Context.Set<RelationalFixtureRow>().SingleAsync()).Name.Should().Be("seed2");
    }

    [Fact]
    public async Task ConcurrentOpenResetAndDisposeAreRefusedDuringAnExplicitReset()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await RelationalRepositoryFixture.CreateAsync<RelationalFixtureContext>(options =>
        {
            options.ConfigureServices = Register;
            options.ResetDatabaseAsync = async (_, _) => { entered.SetResult(); await release.Task; };
        });
        var resetTask = fixture.ResetAsync();
        await entered.Task;
        Func<Task> open = () => fixture.OpenSessionAsync().AsTask();
        Func<Task> secondReset = () => fixture.ResetAsync();
        Func<Task> dispose = () => fixture.DisposeAsync().AsTask();
        await open.Should().ThrowAsync<InvalidOperationException>().WithMessage("*while resetting*");
        await secondReset.Should().ThrowAsync<InvalidOperationException>().WithMessage("*concurrent resets*");
        await dispose.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Await fixture reset*");
        release.SetResult();
        await resetTask;
        await using var session = await fixture.OpenSessionAsync();
    }

    [Fact]
    public async Task FailedResetReleasesTheScopeAndLifecycleGuard()
    {
        var expected = new IOException("reset callback");
        await using var fixture = await RelationalRepositoryFixture.CreateAsync<RelationalFixtureContext>(options =>
        {
            options.ConfigureServices = Register;
            options.ResetDatabaseAsync = (_, _) => Task.FromException(expected);
        });
        Func<Task> reset = () => fixture.ResetAsync();
        (await reset.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(expected);
        fixture.ActiveSessions.Should().Be(0);
        await using var session = await fixture.OpenSessionAsync();
    }

    [Fact]
    public async Task InitializationAndSessionCleanupFailuresAreBothPreservedAndRootResourcesAreDisposed()
    {
        var root = new RelationalFixtureCleanupProbe();
        var initial = new IOException("initialization");
        var cleanup = new IOException("context cleanup");
        Func<Task> create = () => RelationalRepositoryFixture.CreateAsync<RelationalFixtureContext>(options =>
        {
            options.ConfigureServices = services =>
            {
                Register(services);
                services.AddSingleton(_ => root);
                services.AddScoped(provider =>
                {
                    _ = provider.GetRequiredService<RelationalFixtureCleanupProbe>();
                    return new RelationalFixtureContext(provider.GetRequiredService<DbContextOptions<RelationalFixtureContext>>())
                        { BeforeDisposal = () => ValueTask.FromException(cleanup) };
                });
            };
            options.InitializeDatabaseAsync = (_, _) => Task.FromException(initial);
        });
        var failure = (await create.Should().ThrowAsync<AggregateException>()).Which;
        failure.Flatten().InnerExceptions.Should().Contain(initial).And.Contain(cleanup);
        root.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task FixtureDisposalAttemptsEverySessionAndRootEvenWhenCleanupFails()
    {
        var root = new RelationalFixtureCleanupProbe { Failure = new IOException("root cleanup") };
        var fixture = await RelationalRepositoryFixture.CreateAsync<RelationalFixtureContext>(options =>
        {
            options.ConfigureServices = services => { Register(services); services.AddSingleton(_ => root); };
        });
        var first = await fixture.OpenSessionAsync();
        var second = await fixture.OpenSessionAsync();
        _ = first.Services.GetRequiredService<RelationalFixtureCleanupProbe>();
        var firstContext = first.Context;
        var secondContext = second.Context;
        firstContext.BeforeDisposal = () => ValueTask.FromException(new IOException("first"));
        secondContext.BeforeDisposal = () => ValueTask.FromException(new IOException("second"));
        Func<Task> dispose = () => fixture.DisposeAsync().AsTask();
        var failure = (await dispose.Should().ThrowAsync<AggregateException>()).Which;
        failure.Flatten().InnerExceptions.Should().HaveCount(3);
        firstContext.Disposals.Should().Be(1);
        secondContext.Disposals.Should().Be(1);
        root.Disposals.Should().Be(1);
        fixture.ActiveSessions.Should().Be(0);
        await dispose.Should().ThrowAsync<AggregateException>();
        firstContext.Disposals.Should().Be(1);
        Func<Task> open = () => fixture.OpenSessionAsync().AsTask();
        await open.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task ConcurrentSessionDisposalSharesCompletionAndBlocksAccessBeforeUserCleanupRuns()
    {
        await using var fixture = await RelationalRepositoryFixture.CreateAsync<RelationalFixtureContext>(options => options.ConfigureServices = Register);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = await fixture.OpenSessionAsync();
        var context = session.Context;
        context.BeforeDisposal = async () =>
        {
            Action access = () => _ = session.Context;
            access.Should().Throw<ObjectDisposedException>();
            await release.Task;
        };
        var first = session.DisposeAsync().AsTask();
        var second = session.DisposeAsync().AsTask();
        first.Should().BeSameAs(second);
        second.IsCompleted.Should().BeFalse();
        release.SetResult();
        await Task.WhenAll(first, second);
        context.Disposals.Should().Be(1);
        fixture.ActiveSessions.Should().Be(0);
    }

    [Fact]
    public async Task CancellationBeforeCreationDoesNotRegisterAndCancellationDuringSetupSkipsSeeding()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var registered = false;
        Func<Task> create = () => RelationalRepositoryFixture.CreateAsync<RelationalFixtureContext>(options => registered = true, source.Token);
        await create.Should().ThrowAsync<OperationCanceledException>();
        registered.Should().BeFalse();
        using var during = new CancellationTokenSource();
        var seeded = false;
        create = () => RelationalRepositoryFixture.CreateAsync<RelationalFixtureContext>(options =>
        {
            options.ConfigureServices = Register;
            options.InitializeDatabaseAsync = async (_, token) => { token.Should().Be(during.Token); await during.CancelAsync(); };
            options.SeedAsync = (_, _) => { seeded = true; return Task.CompletedTask; };
        }, during.Token);
        await create.Should().ThrowAsync<OperationCanceledException>();
        seeded.Should().BeFalse();
    }

    [Fact]
    public async Task PreCanceledOpenAndResetDoNotChangeFixtureState()
    {
        await using var fixture = await RelationalRepositoryFixture.CreateAsync<RelationalFixtureContext>(options => options.ConfigureServices = Register);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        Func<Task> open = () => fixture.OpenSessionAsync(source.Token).AsTask();
        Func<Task> reset = () => fixture.ResetAsync(source.Token);
        await open.Should().ThrowAsync<OperationCanceledException>();
        await reset.Should().ThrowAsync<OperationCanceledException>();
        fixture.ActiveSessions.Should().Be(0);
    }

    private static void Register(IServiceCollection services)
    {
        services.AddSingleton(_ =>
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            return connection;
        });
        services.AddDbContext<RelationalFixtureContext>((provider, options) => options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
        services.AddScoped<RelationalFixtureRepository>();
    }
}
