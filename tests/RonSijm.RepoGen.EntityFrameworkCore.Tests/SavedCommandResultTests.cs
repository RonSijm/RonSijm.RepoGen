using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.EntityFrameworkCore.Tests;

public sealed class SavedCommandResultTests
{
    [Fact]
    public async Task AsynchronousResultRunsAfterSavingAndCanReadFreshUntrackedState()
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var id = await store.Context.Patients.Select(row => row.Id).FirstAsync();
        var result = await TrackedCommandExecutor.PatchAsync<Patient, string>(store.Context, store.Context.Patients.Where(row => row.Id == id),
            row => { row.Name = "saved"; return [nameof(Patient.Name)]; }, null,
            async (row, token) =>
            {
                await Task.Yield();
                store.Context.Entry(row).State.Should().Be(EntityState.Unchanged);
                return await store.Context.Patients.AsNoTracking().Where(value => value.Id == row.Id).Select(value => value.Name).SingleAsync(token);
            }, true, false, null, []);
        result.Status.Should().Be(CommandStatus.Applied); result.Value.Should().Be("saved");
    }

    [Fact]
    public async Task AsynchronousResultFailuresNeverRestoreAWriteWhichHasAlreadySaved()
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var id = await store.Context.Patients.Select(row => row.Id).FirstAsync();
        Func<Task> execute = async () => await TrackedCommandExecutor.PatchAsync<Patient, string>(store.Context, store.Context.Patients.Where(row => row.Id == id),
            row => { row.Name = "committed"; return [nameof(Patient.Name)]; }, null,
            (_, _) => Task.FromException<string>(new InvalidOperationException("result read failed")), true, false, null, []);
        (await execute.Should().ThrowAsync<RepositorySavedResultException>()).Which.InnerException.Should().BeOfType<InvalidOperationException>();
        (await store.Context.Patients.AsNoTracking().SingleAsync(row => row.Id == id)).Name.Should().Be("committed");
    }

    [Fact]
    public async Task CancellationDuringResultReadIsReportedAsASavedResultFailure()
    {
        await using var store = await SqliteTestStore.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var id = await store.Context.Patients.Select(row => row.Id).FirstAsync();
        Func<Task> execute = async () => await TrackedCommandExecutor.PatchAsync<Patient, string>(store.Context, store.Context.Patients.Where(row => row.Id == id),
            row => { row.Name = "saved before cancellation"; return [nameof(Patient.Name)]; }, null,
            (_, token) => { cancellation.Cancel(); return Task.FromCanceled<string>(token); }, true, false, null, [], cancellation.Token);
        (await execute.Should().ThrowAsync<RepositorySavedResultException>()).Which.InnerException.Should().BeAssignableTo<OperationCanceledException>();
        (await store.Context.Patients.AsNoTracking().SingleAsync(row => row.Id == id)).Name.Should().Be("saved before cancellation");
    }

    [Theory]
    [InlineData(false, CommandStatus.NotFound)]
    [InlineData(true, CommandStatus.NoChanges)]
    public async Task ResultsAreNotQueriedWhenTheCommandDidNotSave(bool exists, CommandStatus expected)
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var id = exists ? await store.Context.Patients.Select(row => row.Id).FirstAsync() : Guid.NewGuid();
        var called = false;
        var result = await TrackedCommandExecutor.PatchAsync<Patient, string>(store.Context, store.Context.Patients.Where(row => row.Id == id),
            _ => [], null, (_, _) => { called = true; return Task.FromResult("unexpected"); }, true, false, null, []);
        result.Status.Should().Be(expected); result.HasValue.Should().BeFalse(); called.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task InvalidSavedReadLifecycleFailsBeforeFetchingOrMutating(bool save, bool reload)
    {
        await using var store = await SqliteTestStore.CreateAsync();
        Action execute = () => TrackedCommandExecutor.PatchAsync<Patient, string>(store.Context, store.Context.Patients,
            row => { row.Name = "unexpected"; return [nameof(Patient.Name)]; }, null, (_, _) => Task.FromResult("unexpected"), save, reload, null, []);
        execute.Should().Throw<ArgumentException>(); store.Context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task CreateSuppliesTheSavedEntityToTheAsynchronousProjection()
    {
        await using var store = await SqliteTestStore.CreateAsync(); var id = Guid.NewGuid();
        var result = await TrackedCommandExecutor.CreateAsync<Patient, Guid>(store.Context,
            () => new Patient { Id = id, Name = "created", Email = "created@example.com", PracticeId = store.PracticeId },
            async (row, token) => await store.Context.Patients.AsNoTracking().Where(value => value.Id == row.Id).Select(value => value.Id).SingleAsync(token),
            true, false, null, []);
        result.Value.Should().Be(id); result.AffectedRows.Should().Be(1);
    }
}
