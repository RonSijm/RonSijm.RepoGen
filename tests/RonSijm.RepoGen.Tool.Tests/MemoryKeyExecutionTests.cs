using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryKeyTestHarness;
using static RonSijm.RepoGen.Tool.Tests.ProxyWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

[Collection("Memory key comparers")]
public sealed class MemoryKeyExecutionTests
{
    [Fact]
    public async Task StoreIdentityDoesNotRewriteGeneratedQueryOrCommandPredicates()
    {
        var builder = MemoryKeyTestHarness.Builder(); builder.Entity<MemoryKeyRow>().HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase);
        var (fixture, repository) = MemoryKeyTestHarness.Fixture(Compile(builder)); var data = Data(fixture); var store = Store(data);
        store.Seed(new("ABC", "old")); store.Find("abc")!.Caption.Should().Be("old");
        (await Invoke<MemoryKeyData?>(repository, "FindAsync", new MemoryKeyRequest("abc"))).Should().BeNull();
        (await Invoke<MemoryKeyData?>(repository, "FindAsync", new MemoryKeyRequest("ABC")))!.Caption.Should().Be("old");
        (await Invoke<CommandResult>(repository, "PatchAsync", new MemoryKeyRequest("abc", "not matched"))).Status.Should().Be(CommandStatus.NotFound);
        await Invoke<CommandResult>(repository, "PatchAsync", new MemoryKeyRequest("ABC", "changed")); store.Find("aBc")!.Caption.Should().Be("changed");
        await Invoke<CommandResult>(repository, "DeleteAsync", new MemoryKeyRequest("ABC")); store.Find("abc").Should().BeNull();
    }

    [Fact]
    public void ArrangementDuplicatePoliciesAndBaselinesPreserveConfiguredKeyIdentity()
    {
        var builder = MemoryKeyTestHarness.Builder(); builder.Entity<MemoryKeyRow>().HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase);
        var (fixture, _) = MemoryKeyTestHarness.Fixture(Compile(builder)); var data = Data(fixture); var store = Store(data);
        store.Seed(new("A", "first")); data.CaptureBaseline();
        Action duplicate = () => store.SeedRange([new("B", "must not persist"), new("a", "duplicate")]); duplicate.Should().Throw<InvalidOperationException>();
        store.Count.Should().Be(1); store.Find("b").Should().BeNull();
        store.Seed(new("a", "ignored"), InMemoryDuplicateKeyBehavior.Ignore); store.Find("a")!.Caption.Should().Be("first");
        store.Seed(new("a", "replacement"), InMemoryDuplicateKeyBehavior.Replace); store.Find("A")!.Caption.Should().Be("replacement");
        store.ReplaceWith([new("C", "replacement set")]); store.Find("c").Should().NotBeNull(); data.RestoreBaseline();
        store.Find("a")!.Caption.Should().Be("first"); store.Find("c").Should().BeNull();
        store.Remove("a").Should().BeTrue(); data.RestoreBaseline(); store.Find("a").Should().NotBeNull(); data.Clear(); store.Count.Should().Be(0);
    }

    [Fact]
    public async Task StagedDuplicateChecksAndCheckpointRestoreUseTheComparer()
    {
        var builder = MemoryKeyTestHarness.Builder(); builder.Entity<MemoryKeyRow>().HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase);
        var (fixture, repository) = MemoryKeyTestHarness.Fixture(Compile(builder)); var data = Data(fixture); var store = Store(data);
        await Invoke<CommandResult>(repository, "StageCreateAsync", new MemoryKeyRequest("A", "first"));
        Func<Task> duplicate = () => Invoke<CommandResult>(repository, "StageCreateAsync", new MemoryKeyRequest("a", "second")); await duplicate.Should().ThrowAsync<InvalidOperationException>();
        data.PendingCount.Should().Be(1); store.Find("a").Should().BeNull(); (await data.SaveChangesAsync()).Should().Be(1); store.Find("a")!.Caption.Should().Be("first");
        await using (var transaction = await data.BeginTransactionAsync())
        {
            await Invoke<CommandResult>(repository, "PatchAsync", new MemoryKeyRequest("A", "transaction"));
            await Invoke<CommandResult>(repository, "CreateAsync", new MemoryKeyRequest("B", "temporary"));
            await transaction.RollbackAsync();
        }
        store.Find("a")!.Caption.Should().Be("first"); store.Find("b").Should().BeNull();
        await Invoke<CommandResult>(repository, "StagePatchAsync", new MemoryKeyRequest("A", "staged")); await data.SaveChangesAsync(); store.Find("a")!.Caption.Should().Be("staged");
        duplicate = () => Invoke<CommandResult>(repository, "CreateAsync", new MemoryKeyRequest("a", "saved duplicate")); await duplicate.Should().ThrowAsync<InvalidOperationException>();
        data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task SaveTimeDuplicateFailureRetainsEarlierStagingAtomically()
    {
        var builder = MemoryKeyTestHarness.Builder(); builder.Entity<MemoryKeyRow>().HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase);
        var (fixture, repository) = MemoryKeyTestHarness.Fixture(Compile(builder)); var data = Data(fixture); var store = Store(data);
        await Invoke<CommandResult>(repository, "StageCreateAsync", new MemoryKeyRequest("A", "pending"));
        store.Seed(new("a", "arranged later")); Func<Task> save = () => data.SaveChangesAsync(); await save.Should().ThrowAsync<InvalidOperationException>();
        data.PendingCount.Should().Be(1); store.Find("A")!.Caption.Should().Be("arranged later");
        store.Remove("A"); (await data.SaveChangesAsync()).Should().Be(1); store.Find("a")!.Caption.Should().Be("pending");
    }

    [Fact]
    public void CustomComparerIsConstructedOnlyOncePerStoreAndNotAtGeneration()
    {
        var builder = MemoryKeyTestHarness.Builder(keyed: true);
        builder.Entity<MemoryKeyRow>().HasInMemoryKeyComparer<string>(() => new MemoryKeyCountingComparer(StringComparer.OrdinalIgnoreCase));
        var before = MemoryKeyCountingComparer.Created; var assembly = Compile(builder); MemoryKeyCountingComparer.Created.Should().Be(before);
        var (fixture, _) = MemoryKeyTestHarness.Fixture(assembly); MemoryKeyCountingComparer.Created.Should().Be(before);
        var first = Data(fixture, "A"); var second = Data(fixture, "a"); MemoryKeyCountingComparer.Created.Should().Be(before + 2);
        Data(fixture, "A").Should().BeSameAs(first); MemoryKeyCountingComparer.Created.Should().Be(before + 2);
        Store(first).Seed(new("KEY", "one")); Store(second).Seed(new("key", "two"));
        Store(first).Find("key")!.Caption.Should().Be("one"); Store(second).Find("KEY")!.Caption.Should().Be("two");
    }

    [Fact]
    public async Task StoreComparerDoesNotChangeContextPartitionIdentity()
    {
        var builder = MemoryKeyTestHarness.Builder(keyed: true); builder.Entity<MemoryKeyRow>().HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase);
        var (fixture, repository) = MemoryKeyTestHarness.Fixture(Compile(builder)); var first = Data(fixture, "A"); var second = Data(fixture, "a");
        await Invoke<CommandResult>(repository, "StageCreateAsync", new MemoryKeyRequest("KEY", "first", "A"));
        await Invoke<CommandResult>(repository, "StageCreateAsync", new MemoryKeyRequest("key", "second", "a"));
        await first.SaveChangesAsync(); Store(first).Find("key")!.Caption.Should().Be("first"); Store(second).Count.Should().Be(0); second.PendingCount.Should().Be(1);
        await second.SaveChangesAsync(); Store(second).Find("KEY")!.Caption.Should().Be("second");
    }

    [Fact]
    public async Task IndependentPublicComparerSupportsCompositeKeysAndGeneratedCommands()
    {
        var builder = MemoryKeyTestHarness.Builder(); var entity = builder.Entity<MemoryKeyCompoundRow>();
        entity.HasInMemoryStore(row => new ValueTuple<string, int>(row.Code, row.Slot),
            row => new MemoryKeyCompoundRow { Code = row.Code, Slot = row.Slot, Title = row.Title })
            .HasInMemoryKeyComparer<ValueTuple<string, int>>(() => new MemoryKeyCompoundComparer());
        entity.HasCommand("Create").Create<MemoryKeyCompoundData>(request => new MemoryKeyCompoundRow { Code = request.Code, Slot = request.Slot, Title = request.Title })
            .AllowWrite(row => row.Code).AllowWrite(row => row.Slot).Save();
        entity.HasCommand("Delete").Where<MemoryKeyCompoundData>((row, request) => row.Code == request.Code && row.Slot == request.Slot).TrackedDelete().Save();
        var assembly = Compile(builder); var (fixture, _) = MemoryKeyTestHarness.Fixture(assembly); var data = Data(fixture);
        var repository = Activator.CreateInstance(assembly.GetType("Generated.MemoryKeys.TestDoubles.MemoryKeyCompoundRowRepositoryTestDouble")!, fixture)!;
        var store = (InMemoryStore<MemoryKeyCompoundRow, ValueTuple<string, int>>)data.GetType().GetProperty("MemoryKeyCompoundRowStore")!.GetValue(data)!;
        store.Seed(new() { Code = "A", Slot = 1, Title = "old" });
        store.Find(("a", 1))!.Title.Should().Be("old"); store.Find(("a", 2)).Should().BeNull();
        Func<Task> duplicate = () => Invoke<CommandResult>(repository, "CreateAsync", new MemoryKeyCompoundData("a", 1, "duplicate")); await duplicate.Should().ThrowAsync<InvalidOperationException>();
        await Invoke<CommandResult>(repository, "CreateAsync", new MemoryKeyCompoundData("A", 2, "new")); store.Find(("a", 2))!.Title.Should().Be("new");
        await Invoke<CommandResult>(repository, "DeleteAsync", new MemoryKeyCompoundData("A", 1, "")); store.Find(("a", 1)).Should().BeNull();
    }

    [Fact]
    public void ReadOnlyStoresAlsoApplyConfiguredKeyIdentity()
    {
        var builder = MemoryKeyTestHarness.Builder(); var entity = builder.Entity<MemoryKeyRow>(); entity.WriteEnabled = false;
        entity.HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase);
        var (fixture, _) = MemoryKeyTestHarness.Fixture(Compile(builder)); var store = Store(Data(fixture));
        store.Seed(new("A", "read only")); store.Find("a")!.Caption.Should().Be("read only");
    }

    [Fact]
    public async Task BatchInputMatchingIsNotRewrittenButSavedIdentityUsesTheConfiguredComparer()
    {
        var builder = MemoryKeyTestHarness.Builder(); var entity = builder.Entity<MemoryKeyRow>();
        entity.HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase);
        entity.HasCommand("PatchMany").PatchMany<MemoryKeyRequest, string, IReadOnlyList<MemoryKeyRequest>>(items => items, row => row.Code, item => item.Key,
            setters => setters.SetProperty(row => row.Title, (row, item) => item.Title), maximumItems: 3).Save();
        var (fixture, repository) = MemoryKeyTestHarness.Fixture(Compile(builder)); var data = Data(fixture); var store = Store(data);
        store.SeedRange([new("A", "first"), new("B", "second")]);
        store.Find("a").Should().NotBeNull();
        (await Invoke<CommandResult>(repository, "PatchManyAsync", (object)new MemoryKeyRequest[] { new("a", "no match") })).Status.Should().Be(CommandStatus.NotFound);
        data.PendingCount.Should().Be(0); store.Find("a")!.Caption.Should().Be("first");
        await Invoke<CommandResult>(repository, "PatchManyAsync", (object)new MemoryKeyRequest[] { new("B", "changed second"), new("A", "changed first") });
        store.Find("a")!.Caption.Should().Be("changed first"); store.Find("b")!.Caption.Should().Be("changed second"); store.Count.Should().Be(2);
    }
}
