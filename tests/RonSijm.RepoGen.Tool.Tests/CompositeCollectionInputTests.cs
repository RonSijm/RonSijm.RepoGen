using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.CompositeCollectionTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionInputTests
{
    [Theory]
    [InlineData("nullCode")]
    [InlineData("nullLanguage")]
    [InlineData("duplicate")]
    public async Task NullComponentsAndDuplicatePairsFailBeforeAnOwnedLeaseCanBeAcquired(string scenario)
    {
        using var context = CompositeCollectionTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseAsyncContextLeases().GenerateOnlyConfiguredEntities());
        builder.Entity<CompositeCollectionParent>().HasCommand("Change").Where<CompositeCollectionRequest>((row, request) => row.Id == request.Id && row.TenantId == request.TenantId)
            .ReconcileCollection<CompositeCollectionChild, CompositeCollectionItem, (string, string), CompositeCollectionRequest>(
                row => row.Children, request => request.Items!, row => new ValueTuple<string, string>(row.Code, row.Language),
                item => new ValueTuple<string, string>(item.Code, item.Language), children => children.MaximumItems(5)
                    .Update(setters => setters.SetProperty(row => row.Text, (row, item) => item.Text))).Save();
        var assembly = SavedResultTestHarness.CompileProduction(context, builder.Build());
        var provider = new CompositeCollectionCountingProvider();
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.CompositeCollectionParentRepositoryOperations")!, provider)!;
        var request = new CompositeCollectionRequest(10, 1, scenario switch
        {
            "nullCode" => [new(1, null!, "en", "sensitive")],
            "nullLanguage" => [new(1, "title", null!, "sensitive")],
            _ => [new(1, "title", "en", "one"), new(2, "title", "en", "two")]
        });
        Func<Task> run = () => Invoke<CommandResult>(repository, "ChangeAsync", request);
        await run.Should().ThrowAsync<ArgumentException>();
        provider.Acquisitions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidSavedMatchingKeysCannotPublishEarlierParentOrChildChanges(bool duplicate)
    {
        var (parent, child, data) = CompositeCollectionTestHarness.Repositories();
        var store = Store<CompositeCollectionChildData, int>(data);
        store.Seed(duplicate ? new(7, 10, 1, "title", "en", "duplicate") : new(7, 10, 1, "invalid", null!, "invalid"));
        await Invoke<CommandResult>(child, "StageTextAsync", 5);
        Func<Task> run = () => Invoke<CommandResult<int>>(parent, "ReplaceAsync", new CompositeCollectionRequest(10, 1, [new(1, "title", "en", "changed")]));
        await run.Should().ThrowAsync<ArgumentException>();
        Store<CompositeCollectionParentData, (int, int)>(data).Find((10, 1))!.Version.Should().Be(1);
        store.Find(1)!.Text.Should().Be("old English"); data.PendingCount.Should().Be(1);
    }
}
