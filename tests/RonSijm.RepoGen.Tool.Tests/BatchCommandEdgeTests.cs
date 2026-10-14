using System.Collections;
using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Caching;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.BatchCommandTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class BatchCommandEdgeTests
{
    [Fact]
    public async Task OneShotInputIsSnapshottedOnceAndTargetsUseOneParameterizedQuery()
    {
        await using var fixture = await CreateAsync();
        var probe = new QueryProbe();
        await using var context = CreateContext(fixture.Connection, probe);
        var input = new OneShotItems([new("B", "Changed B"), new("A", "Changed A")]);
        var operations = Operations(Model(context), context);
        (await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(operations, "Rename", new(1, input))).Succeeded.Should().BeTrue();
        input.Enumerations.Should().Be(1);
        probe.Selects.Should().Be(1);
        probe.Sql.Should().Contain("WHERE").And.Contain("@__").And.NotContain("Changed A");
        probe.Selects = 0;
        var maliciousKey = "A' OR 1=1; --";
        (await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(operations, "Rename", new(1, [new(maliciousKey, "Not SQL")]))).Status.Should().Be(CommandStatus.NotFound);
        probe.Selects.Should().Be(1);
        probe.Sql.Should().NotContain(maliciousKey);
    }

    [Fact]
    public async Task InputCancellationDuringEnumerationStopsBeforeAcquisition()
    {
        using var cancellation = new CancellationTokenSource();
        IEnumerable<BatchItem> Items()
        {
            yield return new("A", "First");
            cancellation.Cancel();
            yield return new("B", "Never mapped");
        }

        Action prepare = () => TrackedBatchCommandExecutor.PrepareInput(Items(), item => item.Code, 10, cancellation.Token);
        prepare.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData("automaticTransactions")]
    [InlineData("savepoints")]
    [InlineData("ambient")]
    public async Task UnsafeTransactionConfigurationsFailBeforeLoadingOrMutation(string scenario)
    {
        await using var fixture = await CreateAsync();
        var operations = Operations(Model(fixture.Context), fixture.Context);
        await using var transaction = scenario == "savepoints" ? await fixture.Context.Database.BeginTransactionAsync() : null;
        if (scenario == "automaticTransactions") fixture.Context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        if (scenario == "savepoints") fixture.Context.Database.AutoSavepointsEnabled = false;
        using var ambient = scenario == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
        Func<Task> invalid = async () => await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(operations, "Rename", new(1, [new("A", "No"), new("B", "No")]));
        await invalid.Should().ThrowAsync<InvalidOperationException>();
        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
        fixture.Context.Saves.Should().Be(0);
    }

    [Fact]
    public async Task ApprovedSettersPersistWithoutAutomaticChangeDetection()
    {
        await using var fixture = await CreateAsync();
        fixture.Context.ChangeTracker.AutoDetectChangesEnabled = false;
        (await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(Operations(Model(fixture.Context, versions: false), fixture.Context), "Rename",
            new(1, [new("A", "Changed A"), new("B", "Changed B")]))).Succeeded.Should().BeTrue();
        (await fixture.Context.Rows.AsNoTracking().Where(row => row.OwnerId == 1).OrderBy(row => row.Id).Select(row => row.Title).ToListAsync()).Should().Equal("Changed A", "Changed B");
        fixture.Context.ChangeTracker.AutoDetectChangesEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SaveFailuresRestoreTargetsAndKeepUnrelatedPendingChanges()
    {
        await using var fixture = await CreateAsync();
        await using var context = CreateContext(fixture.Connection, new SaveFailure());
        var other = await context.Rows.SingleAsync(row => row.Code == "Z");
        other.Title = "Pending";
        Func<Task> failure = async () => await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(Operations(Model(context), context), "Rename", new(1, [new("A", "No"), new("B", "No")]));
        await failure.Should().ThrowAsync<InvalidOperationException>().WithMessage("Injected save failure");
        context.ChangeTracker.Entries<BatchRow>().Where(entry => entry.Entity.Code != "Z").Should().OnlyContain(entry => entry.State == EntityState.Unchanged && entry.Entity.Version == 1);
        other.Title.Should().Be("Pending");
        context.Entry(other).State.Should().Be(EntityState.Modified);
        (await context.Rows.AsNoTracking().SingleAsync(row => row.Code == "A")).Title.Should().Be("First");
    }

    [Fact]
    public async Task ASavedResultFailureKeepsTheWriteAndRestoresLazyLoading()
    {
        await using var fixture = await CreateAsync();
        var builder = Builder();
        builder.Entity<BatchRow>().HasCommand("BadResult")
            .PatchMany<BatchItem, string, BatchRequest>(request => request.Items, row => row.Code, item => item.Code,
                items => items.SetProperty(row => row.Title, (row, item) => item.Title)).Save()
            .Returns(row => new BatchRowView(row.Code, row.Title.Substring(999), row.Version));
        Func<Task> failure = async () => await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(Operations(CreateModel(fixture.Context, builder), fixture.Context), "BadResult", new(1, [new("A", "Saved")]));
        await failure.Should().ThrowAsync<RepositorySavedResultException>();
        (await fixture.Context.Rows.AsNoTracking().SingleAsync(row => row.Code == "A")).Title.Should().Be("Saved");
        fixture.Context.ChangeTracker.LazyLoadingEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LazyLoadingIsSuppressedAndItsPreviousSettingRestored(bool previous)
    {
        await using var fixture = await CreateAsync();
        var probe = new LazyLoadingProbe();
        await using var context = CreateContext(fixture.Connection, probe);
        context.ChangeTracker.LazyLoadingEnabled = previous;
        (await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(Operations(Model(context), context), "Rename", new(1, [new("A", "Saved")]))).Succeeded.Should().BeTrue();
        probe.EnabledDuringSave.Should().BeFalse();
        context.ChangeTracker.LazyLoadingEnabled.Should().Be(previous);
    }

    [Fact]
    public async Task SavedBatchesInvalidateConfiguredCachedReads()
    {
        await using var fixture = await CreateAsync();
        var builder = Builder().Configure(options => options.UseClosedCaching("batch-cache"));
        var entity = builder.Entity<BatchRow>();
        entity.HasQuery("Titles").Cache().OrderBy(row => row.Id).Select(row => row.Title).List();
        entity.HasCommand("Rename").PatchMany<BatchItem, string, BatchRequest>(request => request.Items, row => row.Code, item => item.Code,
            items => items.SetProperty(row => row.Title, (row, item) => item.Title)).Save();
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryRepositoryResultCache(memory);
        var operations = Operations(CreateModel(fixture.Context, builder), fixture.Context, cache);
        async Task<IReadOnlyList<string>> Read() => await (Task<IReadOnlyList<string>>)operations.GetType().GetMethod("TitlesAsync")!.Invoke(operations, [CancellationToken.None])!;
        (await Read()).Should().Equal("First", "Second", "Other owner");
        (await Invoke<CommandResult>(operations, "Rename", new(1, [new("A", "Fresh A"), new("B", "Fresh B")]))).Succeeded.Should().BeTrue();
        (await Read()).Should().Equal("Fresh A", "Fresh B", "Other owner");
    }

    [Fact]
    public async Task APreSaveMapperFailureRestoresEarlierItemsWithoutSaving()
    {
        await using var fixture = await CreateAsync();
        var builder = Builder();
        builder.Entity<BatchRow>().HasCommand("BadMapping")
            .PatchMany<BatchItem, string, BatchRequest>(request => request.Items, row => row.Code, item => item.Code,
                items => items.SetProperty(row => row.Title, (row, item) => item.Title.Substring(1))).Save();
        Func<Task> failure = async () => await Invoke<CommandResult>(Operations(CreateModel(fixture.Context, builder), fixture.Context), "BadMapping",
            new(1, [new("A", "xNo partial change"), new("B", "")]));
        await failure.Should().ThrowAsync<ArgumentOutOfRangeException>();
        fixture.Context.Saves.Should().Be(0);
        fixture.Context.ChangeTracker.Entries<BatchRow>().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        fixture.Context.ChangeTracker.Entries<BatchRow>().Single(entry => entry.Entity.Code == "A").Entity.Title.Should().Be("First");
    }

    [Fact]
    public async Task ASharedSaveConflictOnAnUnrelatedRowIsNotMisattributedToTheBatch()
    {
        await using var fixture = await CreateAsync();
        await using var context = CreateContext(fixture.Connection, new UnrelatedRace());
        var other = await context.Rows.SingleAsync(row => row.Code == "Z");
        other.Title = "Unrelated pending";
        Func<Task> failure = async () => await Invoke<CommandResult<IReadOnlyList<BatchRowView>>>(Operations(Model(context), context), "Rename",
            new(1, [new("A", "No"), new("B", "No")]));
        await failure.Should().ThrowAsync<RepositoryWriteConflictException>();
        context.ChangeTracker.Entries<BatchRow>().Where(entry => entry.Entity.Code != "Z").Should().OnlyContain(entry => entry.State == EntityState.Unchanged && entry.Entity.Version == 1);
        other.Title.Should().Be("Unrelated pending");
        context.Entry(other).State.Should().Be(EntityState.Modified);
        (await context.Rows.AsNoTracking().SingleAsync(row => row.Code == "A")).Title.Should().Be("First");
    }

    [Fact]
    public async Task RegisteredTypeAdaptersExpandInsideItemKeysSettersAndSavedResults()
    {
        await using var fixture = await CreateAsync();
        var builder = Builder();
        var code = builder.HasTypeAdapter<string, BatchCode>(value => new BatchCode(value), value => value.Value);
        builder.Entity<BatchRow>().HasCommand("Strong")
            .Where<StrongBatchRequest>((row, request) => row.OwnerId == request.OwnerId)
            .PatchMany<StrongBatchItem, string, StrongBatchRequest>(request => request.Items, row => row.Code, item => code.ToModel(item.Code),
                items => items.SetProperty(row => row.Title, (row, item) => code.ToContract(row.Code).Value + item.Title)
                    .SetProperty(row => row.Version, row => row.Version + 1)
                    .ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion))
            .AllowWrite(row => row.Version).Save().Returns(row => new StrongBatchView(code.ToContract(row.Code), row.Title, row.Version));
        var model = CreateModel(fixture.Context, builder);
        RepositorySourceRenderer.Render(model).Should().NotContain(".ToModel(").And.NotContain(".ToContract(");
        var operations = Operations(model, fixture.Context);
        var result = await (Task<CommandResult<IReadOnlyList<StrongBatchView>>>)operations.GetType().GetMethod("StrongAsync")!.Invoke(operations,
            [new StrongBatchRequest(1, [new(new("A"), " changed", 1)]), CancellationToken.None])!;
        result.Value.Should().Equal(new StrongBatchView(new("A"), "A changed", 2));
    }

    private sealed class OneShotItems(BatchItem[] items) : IEnumerable<BatchItem>
    {
        internal int Enumerations { get; private set; }
        public IEnumerator<BatchItem> GetEnumerator()
        {
            if (++Enumerations != 1) throw new InvalidOperationException("Input was enumerated twice.");
            return ((IEnumerable<BatchItem>)items).GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class QueryProbe : DbCommandInterceptor
    {
        internal int Selects { get; set; }
        internal string Sql { get; private set; } = "";
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Selects++;
                Sql = command.CommandText;
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class SaveFailure : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Injected save failure");
    }

    private sealed class UnrelatedRace : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await eventData.Context!.Database.ExecuteSqlRawAsync("UPDATE Rows SET Version = Version + 1 WHERE Code = 'Z'", cancellationToken);
            return result;
        }
    }

    private sealed class LazyLoadingProbe : SaveChangesInterceptor
    {
        internal bool? EnabledDuringSave { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            EnabledDuringSave = eventData.Context!.ChangeTracker.LazyLoadingEnabled;
            return ValueTask.FromResult(result);
        }
    }
}
