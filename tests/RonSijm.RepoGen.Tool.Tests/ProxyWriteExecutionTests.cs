using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ProxyWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

[Collection("Proxy writes")]
public sealed class ProxyWriteExecutionTests
{
    [Fact]
    public async Task InternalEntitiesAreNeverConstructedWhilePublicRowsReceiveGeneratedSavedValues()
    {
        var before = ProxyWriteRecord.Constructions; var (repository, _, _, store) = Fixture();
        var created = await Invoke<CommandResult<ProxyWriteView>>(repository, "CreateAsync", new ProxyWriteRequest(0, "new"));
        created.Value.Should().BeEquivalentTo(new ProxyWriteView(9, "new", 3, "fixture", []));
        store.Find(9)!.Extra.Should().Be("created");
        var renamed = await Invoke<CommandResult<ProxyWriteView>>(repository, "RenameAsync", new ProxyWriteRequest(7, "longer", Stamp: [4, 5]));
        renamed.Value!.Version.Should().Be(6); store.Find(7)!.Caption.Should().Be("longer"); store.Find(7)!.Extra.Should().Be("arranged");
        ProxyWriteRecord.Constructions.Should().Be(before);
    }

    [Fact]
    public async Task MultipleMappedRepositoriesShareStageSaveAndReservedGeneratedKeys()
    {
        var (repository, audit, data, store) = Fixture();
        await Invoke<CommandResult>(repository, "StageCreateAsync", new ProxyWriteRequest(0, "first"));
        await Invoke<CommandResult>(repository, "StageCreateAsync", new ProxyWriteRequest(0, "second"));
        await Invoke<CommandResult>(audit, "StageAuditAsync", new ProxyWriteRequest(1, "audit"));
        await Invoke<CommandResult>(repository, "StageRenameAsync", new ProxyWriteRequest(7, "saved"));
        store.Count.Should().Be(2); store.Find(7)!.Caption.Should().Be("old"); data.PendingCount.Should().Be(4);
        (await data.SaveChangesAsync()).Should().Be(4);
        store.Find(9)!.Caption.Should().Be("first"); store.Find(10)!.Caption.Should().Be("second"); store.Find(7)!.Revision.Should().Be(5);
        ((InMemoryStore<ProxyWriteAuditData, int>)data.GetType().GetProperty("ProxyWriteAuditStore")!.GetValue(data)!).Find(1)!.Caption.Should().Be("audit");
    }

    [Fact]
    public async Task MissingHiddenStaleAndUnsuppliedCommandsDoNotPublishState()
    {
        var (repository, _, data, store) = Fixture();
        (await Invoke<CommandResult<ProxyWriteView>>(repository, "RenameAsync", new ProxyWriteRequest(99, "missing"))).Status.Should().Be(CommandStatus.NotFound);
        (await Invoke<CommandResult<ProxyWriteView>>(repository, "DeleteAsync", new ProxyWriteRequest(8, "hidden", 6))).Status.Should().Be(CommandStatus.NotFound);
        (await Invoke<CommandResult<ProxyWriteView>>(repository, "RenameAsync", new ProxyWriteRequest(7, "stale", 0))).Status.Should().Be(CommandStatus.Conflict);
        (await Invoke<CommandResult<ProxyWriteView>>(repository, "RenameAsync", new ProxyWriteRequest(7, "unused", Supplied: false))).Status.Should().Be(CommandStatus.NoChanges);
        data.PendingCount.Should().Be(0); store.Find(7)!.Caption.Should().Be("old");
    }

    [Fact]
    public async Task RequestsResultsSavedRowsAndBaselinesOwnTheirBinaryValues()
    {
        var (repository, _, data, store) = Fixture(); data.CaptureBaseline(); var bytes = new byte[] { 4, 5 };
        await Invoke<CommandResult>(repository, "StageRenameAsync", new ProxyWriteRequest(7, "saved", Stamp: bytes)); bytes[0] = 99;
        await data.SaveChangesAsync(); store.Find(7)!.Binary.Should().Equal(4, 5);
        var read = (await Invoke<IReadOnlyList<ProxyWriteView>>(repository, "ListAsync")).Single(); read.Stamp[0] = 100;
        store.Find(7)!.Binary.Should().Equal(4, 5);
        data.RestoreBaseline(); store.Find(7)!.Binary.Should().Equal(1, 2);
    }

    [Fact]
    public async Task BatchesKeepInputOrderGenerateSavedTokensAndRejectStaleLastTargetsAtomically()
    {
        var (repository, _, data, store) = Fixture();
        await Invoke<CommandResult<ProxyWriteView>>(repository, "CreateAsync", new ProxyWriteRequest(20, "second"));
        IReadOnlyList<ProxyWriteRequest> stale = [new(20, "changed", 6), new(7, "bad", 0)];
        (await Invoke<CommandResult<IReadOnlyList<ProxyWriteView>>>(repository, "RenameManyAsync", stale)).Status.Should().Be(CommandStatus.Conflict);
        store.Find(20)!.Caption.Should().Be("second"); data.PendingCount.Should().Be(0);
        IReadOnlyList<ProxyWriteRequest> items = [new(20, "one", 6), new(7, "four", 3)];
        var patched = await Invoke<CommandResult<IReadOnlyList<ProxyWriteView>>>(repository, "RenameManyAsync", items);
        patched.Value!.Select(row => row.Id).Should().Equal(20, 7); patched.Value!.Select(row => row.Version).Should().Equal(3, 4);
        var deleted = await Invoke<CommandResult<IReadOnlyList<ProxyWriteView>>>(repository, "DeleteManyAsync", (IReadOnlyList<ProxyWriteRequest>)[new(20, "", 3), new(7, "", 4)]);
        deleted.Value!.Select(row => row.Id).Should().Equal(20, 7); store.Count.Should().Be(1);
    }

    [Fact]
    public async Task CheckpointAndBaselineReplayRestoreRowsAndGeneratedAllocationWithoutCounters()
    {
        var (repository, _, data, store) = Fixture(); data.CaptureBaseline();
        await using (var transaction = await data.BeginTransactionAsync())
        {
            await Invoke<CommandResult<ProxyWriteView>>(repository, "CreateAsync", new ProxyWriteRequest(0, "rolled"));
            await Invoke<CommandResult>(repository, "StageDeleteAsync", new ProxyWriteRequest(7, ""));
            await transaction.RollbackAsync();
        }
        data.PendingCount.Should().Be(0); store.Count.Should().Be(2);
        (await Invoke<CommandResult<ProxyWriteView>>(repository, "CreateAsync", new ProxyWriteRequest(0, "again"))).Value!.Id.Should().Be(9);
        data.RestoreBaseline();
        (await Invoke<CommandResult<ProxyWriteView>>(repository, "CreateAsync", new ProxyWriteRequest(0, "replay"))).Value!.Id.Should().Be(9);
    }

    [Fact]
    public async Task ScriptsStillOverrideAutomaticMappedWritesAndResetRetainsArrangementData()
    {
        var (repository, _, data, store) = Fixture();
        ScriptedTestDoubleTestHarness.Returns(repository, "OnRenameAsync", new CommandResult<ProxyWriteView>(CommandStatus.Conflict));
        (await Invoke<CommandResult<ProxyWriteView>>(repository, "RenameAsync", new ProxyWriteRequest(7, "unused"))).Status.Should().Be(CommandStatus.Conflict);
        data.PendingCount.Should().Be(0); store.Find(7)!.Caption.Should().Be("old");
        repository.GetType().GetMethod("ResetTestDouble")!.Invoke(repository, null);
        (await Invoke<CommandResult<ProxyWriteView>>(repository, "RenameAsync", new ProxyWriteRequest(7, "new"))).Status.Should().Be(CommandStatus.Applied);
        store.Count.Should().Be(2);
    }
}
