using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class LinkWriteInputTests
{
    [Theory]
    [InlineData("nullList")]
    [InlineData("nullItem")]
    [InlineData("nullRegion")]
    [InlineData("nullCode")]
    [InlineData("duplicate")]
    [InlineData("oversized")]
    public async Task InvalidCompositeReferencesFailBeforeAnyOwnedContextCanBeAcquired(string invalid)
    {
        using var context = LinkWriteTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(LinkWriteTestHarness.Namespace + ".Contracts").UseGeneratedNamespace(LinkWriteTestHarness.Namespace + ".Persistence").UseAsyncContextLeases());
        builder.Entity<LinkWriteOwner>().HasCommand("Change").Where<LinkWriteRequest>((row, request) => row.Id == request.OwnerId && row.TenantId == request.TenantId)
            .ReconcileLinks<LinkWriteRow, LinkWriteItem, (string, string), LinkWriteRequest>(row => row.Links, request => request.Items!,
                row => new ValueTuple<string, string>(row.TargetRegion, row.TargetCode), item => new ValueTuple<string, string>(item.Region, item.Code),
                links => links.MaximumItems(2).MissingItems(RepositoryCollectionRemovalBehavior.Delete)).Save();
        var assembly = SavedResultTestHarness.CompileProduction(context, builder.Build());
        var provider = new LinkWriteCountingProvider();
        var repository = Activator.CreateInstance(assembly.GetType(LinkWriteTestHarness.Namespace + ".Persistence.LinkWriteOwnerRepositoryOperations")!, provider)!;
        IReadOnlyList<LinkWriteItem>? items = invalid switch
        {
            "nullList" => null, "nullItem" => [null!], "nullRegion" => [new(null!, "a", "value")], "nullCode" => [new("eu", null!, "value")],
            "duplicate" => [new("eu", "a", "one"), new("eu", "a", "two")],
            _ => [new("eu", "a", "one"), new("eu", "b", "two"), new("us", "a", "three")]
        };
        Func<Task> invoke = () => Invoke<CommandResult>(repository, "ChangeAsync", new LinkWriteRequest(1, 1, items));
        await invoke.Should().ThrowAsync<ArgumentException>(); provider.Acquisitions.Should().Be(0);
    }
}
