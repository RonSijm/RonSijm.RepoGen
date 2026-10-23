using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedKeySavedResultTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedSingleAndBatchResultsCanReadRelatedKeysWithoutEnablingMappedWriteState(bool batch)
    {
        await using var context = ManyReadTestHarness.Context();
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.AddRange(ManyReadTestHarness.Owners());
        context.AddRange(ManyReadTestHarness.Tags());
        context.AddRange(ManyReadTestHarness.Links());
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = ManyReadTestHarness.Builder();
        builder.Entity<ManyReadLink>().MapInMemoryEntity<ManyReadLinkData>(row => new ManyReadLink
            { Id = row.Id, TenantId = row.TenantId, OwnerCode = row.OwnerCode, Hidden = row.Hidden })
            .MapInMemoryProperty<ManyReadLinkData, int?>(nameof(ManyReadLink.TagId), row => row.TagId);
        builder.Entity<ManyReadOwner>().HasInMemoryNavigation(row => row.Tags, useMappedPropertyKeys: true);
        builder.Entity<ManyReadTag>().HasInMemoryNavigation(row => row.Owners, useMappedPropertyKeys: true);
        var design = builder.Build();
        var fakeAssembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, design));
        var (fake, _, _) = ManyReadTestHarness.Repositories(assembly: fakeAssembly);
        var realAssembly = SavedResultTestHarness.CompileProduction(context, design);
        var real = Activator.CreateInstance(realAssembly.GetType("Generated.ManyRead.Persistence.ManyReadOwnerRepositoryOperations")!, context)!;
        if (batch)
        {
            var items = new List<ManyReadPatch> { new(1, "renamed"), new(2, "updated") };
            var actual = await ManyReadTestHarness.Invoke<Task<CommandResult<IReadOnlyList<ManyReadCard>>>>(fake, "RenameManyAsync", items);
            var expected = await ManyReadTestHarness.Invoke<Task<CommandResult<IReadOnlyList<ManyReadCard>>>>(real, "RenameManyAsync", items);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected));
        }
        else
        {
            var actual = await ManyReadTestHarness.Invoke<Task<CommandResult<ManyReadCard>>>(fake, "RenameAsync", 1);
            var expected = await ManyReadTestHarness.Invoke<Task<CommandResult<ManyReadCard>>>(real, "RenameAsync", 1);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected));
        }
    }
}
