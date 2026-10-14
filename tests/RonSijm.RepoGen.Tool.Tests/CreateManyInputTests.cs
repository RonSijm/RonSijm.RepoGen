using System.Reflection;
using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CreateManyInputTests
{
    private static readonly Lazy<Assembly> Production = new(() =>
    {
        using var context = CreateManyTestHarness.Context();
        return SavedResultTestHarness.CompileProduction(context, new CreateManyRepositoryDesigner(keyed: true).CreateModel());
    });

    [Theory]
    [InlineData("empty")]
    [InlineData("null-list")]
    [InlineData("null-item")]
    [InlineData("limit")]
    public async Task EmptyAndInvalidInputsFinishBeforeKeyedLeaseAcquisition(string mode)
    {
        var provider = new CreateManyCountingProvider();
        var repository = Activator.CreateInstance(Production.Value.GetType("Generated.CreateMany.Persistence.CreateManyRowRepositoryOperations")!, provider)!;
        var request = new CreateManyRequest(mode switch
        {
            "empty" => [], "null-list" => null!, "null-item" => [null!],
            _ => Enumerable.Repeat(new CreateManyItem(7, "never"), 4).ToArray()
        });
        if (mode == "empty") (await Invoke<CommandResult>(repository, "CountAsync", request)).Status.Should().Be(CommandStatus.NoChanges);
        else
        {
            Func<Task> call = () => Invoke<CommandResult>(repository, "CountAsync", request);
            await call.Should().ThrowAsync<ArgumentException>();
        }
        provider.Acquisitions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PopulatedConstructorGraphsAreRejectedBeforeAnyRowIsAttached(bool collection)
    {
        using var context = CreateManyTestHarness.Context(); context.ChangeTracker.LazyLoadingEnabled = false;
        Func<Task> call = collection
            ? () => TrackedCreateManyExecutor.ExecuteAsync<CreateManyParent, int, int>(context, [1], _ => new() { Id = 1, Rows = [new CreateManyRow { Name = "graph" }] }, row => row.Id, false, null, [])
            : () => TrackedCreateManyExecutor.ExecuteAsync<CreateManyRow, int, int>(context, [1], _ => new() { Name = "graph", Parent = new CreateManyParent { Id = 1 } }, row => row.Id, false, null, []);
        await call.Should().ThrowAsync<InvalidOperationException>().WithMessage("*populated navigation graphs*");
        context.ChangeTracker.Entries().Should().BeEmpty(); context.ChangeTracker.LazyLoadingEnabled.Should().BeFalse();
    }
}
