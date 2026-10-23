using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ContextFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ContextFilterExecutionTests
{
    private static readonly int[] ScriptedResult = [42];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AllReadRootsAndDeclaredNavigationGraphsUseOneKeyValuePerOperation(bool mapped)
    {
        var key = new ContextFilterKey("A"); var (row, folder, data, fixture) = Fixture(key, mapped);
        var input = new ContextFilterRequest(key);
        (await Call<IReadOnlyList<ModelFilterCard>>(row, "Cards", input)).Select(item => item.Id).Should().Equal(1, 5, 6);
        input.KeyReads.Should().Be(1);
        (await Call<IReadOnlyList<int>>(row, "Joined", new ContextFilterRequest(key))).Should().Equal(1);
        (await Call<IReadOnlyList<ContextFilterCard>>(row, "Related", new ContextFilterRequest(key)))
            .Should().Equal(new ContextFilterCard(1, "A", 1), new(5, "A", null), new(6, "A", null));
        (await Call<IReadOnlyList<int>>(folder, "Collections", new ContextFilterRequest(key))).Should().Equal(2);
        var page = await Call<PagedResult<ModelFilterCard>>(row, "Page", new ContextFilterRequest(key), 2, 1);
        page.TotalCount.Should().Be(3); page.Items.Single().Id.Should().Be(5);
        (await Call<int>(row, "Count", new ContextFilterRequest(key))).Should().Be(3);
        (await Call<IReadOnlyList<ModelFilterCard>>(row, "All", new ContextFilterRequest(key))).Select(item => item.Id).Should().Equal(1, 2, 3, 5, 6);
        (await Call<IReadOnlyList<ModelFilterCard>>(row, "Bypass", new ContextFilterRequest(key))).Select(item => item.Id).Should().Equal(1, 4, 5, 6);
        var secondKey = new ContextFilterKey("B");
        var second = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, [secondKey])!; Seed(second, mapped);
        (await Call<IReadOnlyList<ModelFilterCard>>(row, "Cards", new ContextFilterRequest(secondKey))).Select(item => item.Id).Should().Equal(2);
        second.Should().NotBeSameAs(data);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FilteredCommandsBatchesAndFreshSavedResultsRetainTheirSeparateSemantics(bool mapped)
    {
        var key = new ContextFilterKey("A"); var (row, _, data, _) = Fixture(key, mapped);
        (await Call<CommandResult<ModelFilterCard>>(row, "Rename", new ContextFilterRequest(key, 2))).Status.Should().Be(CommandStatus.NotFound);
        var graphInput = new ContextFilterRequest(key, 5);
        (await Call<CommandResult<ContextFilterCard>>(row, "RenameGraphSaved", graphInput)).Value.Should().Be(new ContextFilterCard(5, "A", null));
        graphInput.KeyReads.Should().Be(1);
        var batch = new ContextFilterRequest(key) { Items = [new(key, 1), new(key, 5)] };
        (await Call<CommandResult<IReadOnlyList<ModelFilterCard>>>(row, "RenameMany", batch)).Value!.Select(item => item.Id).Should().Equal(1, 5);
        batch.KeyReads.Should().Be(1);
        var failed = new ContextFilterRequest(key) { Items = [new(key, 1), new(key, 2)] };
        (await Call<CommandResult<IReadOnlyList<ModelFilterCard>>>(row, "RenameMany", failed)).Status.Should().Be(CommandStatus.NotFound);
        data.PendingCount.Should().Be(0);
        (await Call<CommandResult>(row, "StageRename", new ContextFilterRequest(key, 6, "B"))).Status.Should().Be(CommandStatus.Staged);
        var immediateInput = new ContextFilterRequest(key);
        (await Call<MutationResult<ModelFilterCard>>(row, "Archive", immediateInput)).Items.Select(item => item.Id).Should().Equal(1, 5, 6);
        immediateInput.KeyReads.Should().Be(1); data.PendingCount.Should().Be(1);
        (await Call<int>(row, "Count", new ContextFilterRequest(key))).Should().Be(0);
        data.DiscardChanges();
        Seed(data, mapped);
        Func<Task> filteredSavedResult = async () => await Call<CommandResult<ModelFilterCard>>(row, "RenameSaved", new ContextFilterRequest(key, 1, "B"));
        await filteredSavedResult.Should().ThrowAsync<RepositorySavedResultException>();
        (await Call<IReadOnlyList<ModelFilterCard>>(row, "All", new ContextFilterRequest(key, 1))).Single().Title.Should().Be("B");
        (await Call<int>(row, "Count", new ContextFilterRequest(key))).Should().Be(2);
        (await Call<int>(row, "Consume", new ContextFilterRequest(key, 2))).Should().Be(0);
    }

    [Fact]
    public async Task ValidationCancellationAndScriptsDoNotResolveKeysOrCreatePartitions()
    {
        using var context = Context(); var builder = Base();
        builder.Entity<ModelFilterRow>().HasQuery("Read").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key)
            .Validate<ContextFilterRequest>(input => input.Id > 0, "An id is required.").Select(item => item.Id).List();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var (row, _, _, _) = Fixture(assembly: assembly); var input = new ContextFilterRequest(new("A"));
        Func<Task> invalid = async () => await Call<IReadOnlyList<int>>(row, "Read", input);
        await invalid.Should().ThrowAsync<ArgumentException>(); input.KeyReads.Should().Be(0);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var task = (Task<IReadOnlyList<int>>)row.GetType().GetMethod("ReadAsync")!.Invoke(row, [input, cancellation.Token])!;
        Func<Task> cancelled = async () => await task; await cancelled.Should().ThrowAsync<OperationCanceledException>(); input.KeyReads.Should().Be(0);
        var script = row.GetType().GetProperty("OnReadAsync")!.GetValue(row)!;
        script.GetType().GetMethod("Returns")!.Invoke(script, [ScriptedResult]);
        (await Call<IReadOnlyList<int>>(row, "Read", input)).Should().Equal(42); input.KeyReads.Should().Be(0);
    }

    [Fact]
    public async Task PortableScalarRecipesUseTheChosenKeyRatherThanAContextValue()
    {
        using var context = Context(); var builder = Base(bound: false);
        builder.HasInMemoryModelFilterContext<ContextFilterContext, ContextFilterKey, string>(value => value.Tenant, key => key.Tenant.ToUpperInvariant());
        builder.HasInMemoryModelFilterContext<ContextFilterContext, ContextFilterKey, bool>(value => value.IncludeDeleted, key => key.IncludeDeleted);
        builder.Entity<ModelFilterRow>().HasQuery("Read").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key).Select(row => row.Id).List();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var key = new ContextFilterKey("a"); var (repository, _, _, _) = Fixture(key, assembly: assembly);
        (await Call<IReadOnlyList<int>>(repository, "Read", new ContextFilterRequest(key))).Should().Equal(1, 5, 6);
    }

    [Fact]
    public async Task ColdStreamsResolveOneKeyAtEnumerationAndKeepADetachedFilteredSnapshot()
    {
        using var context = Context(); var builder = Base();
        builder.Entity<ModelFilterRow>().HasQuery("Read").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key)
            .OrderBy(row => row.Id).Select(row => row.Id).Stream();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var key = new ContextFilterKey("A"); var (row, _, data, _) = Fixture(key, assembly: assembly);
        var input = new ContextFilterRequest(key);
        var stream = InMemoryQueryTestHarness.Invoke<IAsyncEnumerable<int>>(row, "ReadAsync", input);
        input.KeyReads.Should().Be(0);
        await using var enumerator = stream.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue(); enumerator.Current.Should().Be(1); input.KeyReads.Should().Be(1);
        ModelFilterTestHarness.Store<ModelFilterRowSeed>(data, "ModelFilterRow").Clear();
        var rest = new List<int>(); while (await enumerator.MoveNextAsync()) rest.Add(enumerator.Current);
        rest.Should().Equal(5, 6); input.KeyReads.Should().Be(1);
    }
}
