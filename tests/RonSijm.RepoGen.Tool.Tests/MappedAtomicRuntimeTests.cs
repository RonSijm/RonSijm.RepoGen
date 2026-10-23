using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedAtomicTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedAtomicRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringResultMappingDoesNotReportSuccessAndKeepsTransactionOwnership(bool borrowed)
    {
        await using var context = Context(); await SeedAsync(context);
        await using var transaction = borrowed ? await context.Database.BeginTransactionAsync() : null;
        using var cancellation = new CancellationTokenSource();
        Func<Task> action = () => AtomicWriteExecutor.ExecuteWithMappedStateAsync<MappedStateNote, int>(context, new SqliteAtomicWriter(),
            () => new MappedStateNote { Id = 20, Title = "canceled", Counter = 1 }, ["Id", "Title", "Counter"], ["Title"], null,
            row => { cancellation.Cancel(); return row.GetValue<int>("Revision"); }, null, [], cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        (await context.Set<MappedStateNote>().AnyAsync(row => row.Id == 20)).Should().Be(borrowed);
        if (transaction is not null) await transaction.RollbackAsync();
        (await context.Set<MappedStateNote>().AnyAsync(row => row.Id == 20)).Should().BeFalse();
    }

    [Fact]
    public async Task NullReturnedFieldsReplaceConstructorStateAndMappingFailuresRollBackOwnedInsertion()
    {
        await using var context = ReturningMappedTestHarness.Empty();
        await context.Database.OpenConnectionAsync(); await context.Database.EnsureCreatedAsync();
        var result = await AtomicWriteExecutor.ExecuteWithMappedStateAsync<ReturningMappedRow, ReturningMappedRow>(context, new SqliteAtomicWriter(),
            () => new ReturningMappedRow { Id = 1, Title = "new", Quantity = 1 }, ["Id", "Title", "Quantity"], ["Id"], null,
            row => row.Entity, null, []);
        result.Value!.StateValue().Should().BeNull(); result.Value.FingerprintValue().Should().BeNull();
        ReturningMappedRow.FailIndexerAssignment.Value = true;
        try
        {
            Func<Task> failure = () => AtomicWriteExecutor.ExecuteWithMappedStateAsync<ReturningMappedRow, int>(context, new SqliteAtomicWriter(),
                () => new ReturningMappedRow { Id = 2, Title = "rejected", Quantity = 1 }, ["Id", "Title", "Quantity"], ["Id"], null,
                row => row.GetValue<int>("audit"), null, []);
            await failure.Should().ThrowAsync<Exception>();
        }
        finally { ReturningMappedRow.FailIndexerAssignment.Value = false; }
        (await context.Rows.IgnoreQueryFilters().Select(row => row.Id).ToListAsync()).Should().Equal(1);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ProviderMaterializesFieldsIndexersAndShadowBufferWithoutAttachingTheCandidate()
    {
        await using var context = Context(); await SeedAsync(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var candidate = new MappedStateNote { Id = 20, Title = "native", Counter = 4 };
        var result = await new SqliteAtomicWriter().InsertIfAbsentWithMappedStateAsync(context, candidate, ["Id", "Title", "Counter"], ["Title"]);
        result.Status.Should().Be(CommandStatus.Inserted);
        result.Value!.GetValue<int>("Revision").Should().Be(5);
        result.Value.GetValue<string>("Caption").Should().Be("NATIVE");
        result.Value.Entity.ReadAudit().Should().Be(14);
        result.Value.Entity["Stamp"].Should().BeEquivalentTo(new byte[] { 1, 2 });
        result.Value.Entity.Caption.Should().Be("display:NATIVE");
        context.ChangeTracker.Entries().Should().BeEmpty();
        await transaction.RollbackAsync();
        (await context.Set<MappedStateNote>().AnyAsync(row => row.Id == 20)).Should().BeFalse();
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("shadow")]
    [InlineData("fieldAssignment")]
    [InlineData("shadowAssignment")]
    public async Task DirectProviderCallsCannotBroadenCaptureOrAssignmentPermissions(string mode)
    {
        await using var context = Context(); await SeedAsync(context);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var candidate = new MappedStateNote { Id = 20, Title = "new" };
        var writer = new SqliteAtomicWriter();
        Func<Task> action = mode switch
        {
            "ordinary" => () => writer.InsertIfAbsentAsync(context, candidate, ["Id", "Title", "Counter"], ["Title"]),
            "shadow" => () => writer.InsertIfAbsentWithStateAsync(context, candidate, ["Id", "Title", "Counter"], ["Title"]),
            "fieldAssignment" => () => writer.InsertIfAbsentWithMappedStateAsync(context, candidate, ["Id", "Title", "audit"], ["Title"]),
            _ => () => writer.InsertIfAbsentWithMappedStateAsync(context, candidate, ["Id", "Title", "Revision"], ["Title"])
        };
        await action.Should().ThrowAsync<NotSupportedException>();
        (await context.Set<MappedStateNote>().AnyAsync(row => row.Id == 20)).Should().BeFalse();
    }

    [Fact]
    public async Task AProjectionFailureInsideACallerOwnedTransactionStillRequiresCallerRollback()
    {
        await using var context = Context(); await SeedAsync(context);
        var assembly = ImmediateTestHarness.CompileProduction(context, Builder());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MappedStateNoteRepositoryOperations")!, context, new SqliteAtomicWriter())!;
        await using var transaction = await context.Database.BeginTransactionAsync();
        Func<Task> failure = () => Call<CommandResult<MappedAtomicFailure>>(repository, "FailEnsure", new(20, "new"));
        await failure.Should().ThrowAsync<InvalidOperationException>().WithMessage("atomic mapped result failed:*");
        (await context.Set<MappedStateNote>().AnyAsync(row => row.Id == 20)).Should().BeTrue();
        await transaction.RollbackAsync();
        (await context.Set<MappedStateNote>().AnyAsync(row => row.Id == 20)).Should().BeFalse();
        context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
