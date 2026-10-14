using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MultipleSharedLinkTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("firstMismatch")]
    [InlineData("secondMismatch")]
    public async Task NonAdjacentSharedComponentsRespectAlternateTargetKeysAndDifferentOwnerOrder(string shape)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new MultipleSharedLinkContext(new DbContextOptionsBuilder<MultipleSharedLinkContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Add(new MultipleSharedLinkOwner { Zone = 9, SpaceId = 7, Id = 1 });
        context.Add(new MultipleSharedLinkTarget { Id = 1, Region = 7, Code = "a", Segment = 9 });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var builder = Builder();
        var production = SavedResultTestHarness.CompileProduction(context, builder.Build());
        var real = Activator.CreateInstance(production.GetType("Generated.MultipleSharedLinks.Persistence.MultipleSharedLinkOwnerRepositoryOperations")!, context)!;
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle);
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.MultipleSharedLinks.TestDoubles.MultipleSharedLinkContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        ManyReadTestHarness.Store<MultipleSharedLinkOwner, (int, int, int)>(data, nameof(MultipleSharedLinkOwner)).Seed(new() { Zone = 9, SpaceId = 7, Id = 1 });
        var fake = Activator.CreateInstance(assembly.GetType("Generated.MultipleSharedLinks.TestDoubles.MultipleSharedLinkOwnerRepositoryTestDouble")!, fixture)!;
        var model = RepositoryModelFactory.Create(context, typeof(MultipleSharedLinkContext).Assembly, design: builder.Build());
        var shared = model.Entities.Single(entity => entity.EntityName == nameof(MultipleSharedLinkOwner)).Commands.Single().Collections!.Single().Link!.SharedReferenceColumns!;
        shared.Select(column => (column.PropertyName, column.OwnerPropertyName, column.ReferenceKeyIndex)).Should()
            .Equal((nameof(MultipleSharedLinkRow.Scope1), nameof(MultipleSharedLinkOwner.SpaceId), 0), (nameof(MultipleSharedLinkRow.Scope2), nameof(MultipleSharedLinkOwner.Zone), 2));
        var request = new MultipleSharedLinkRequest(9, 7, 1, [new(shape == "firstMismatch" ? 8 : 7, "a", shape == "secondMismatch" ? 10 : 9)]);
        foreach (var repository in new[] { fake, real })
        {
            if (shape == "valid") (await Invoke<CommandResult<int>>(repository, "MergeAsync", request)).AffectedRows.Should().Be(1);
            else
            {
                Func<Task> reject = async () => await Invoke<CommandResult<int>>(repository, "MergeAsync", request);
                await reject.Should().ThrowAsync<ArgumentException>().WithMessage("*selected owner*");
            }
        }
        var store = ManyReadTestHarness.Store<MultipleSharedLinkRow, (int, int, int, string)>(data, nameof(MultipleSharedLinkRow));
        store.Count.Should().Be(shape == "valid" ? 1 : 0);
        context.Set<MultipleSharedLinkRow>().Count().Should().Be(store.Count); data.PendingCount.Should().Be(0);
    }

    private static RepositoryModelBuilder Builder()
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Generated.MultipleSharedLinks.Contracts").UseGeneratedNamespace("Generated.MultipleSharedLinks.Persistence")
            .UseScriptedTestDoubles("Generated.MultipleSharedLinks.TestDoubles").UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands());
        builder.Entity<MultipleSharedLinkRow>().HasInMemoryStore(row => new ValueTuple<int, int, int, string>(row.Scope1, row.Scope2, row.OwnerId, row.TargetCode),
            row => new MultipleSharedLinkRow { Scope1 = row.Scope1, Scope2 = row.Scope2, OwnerId = row.OwnerId, TargetCode = row.TargetCode, Caption = row.Caption });
        builder.Entity<MultipleSharedLinkOwner>().HasInMemoryStore(row => new ValueTuple<int, int, int>(row.Zone, row.Id, row.SpaceId),
            row => new MultipleSharedLinkOwner { Zone = row.Zone, Id = row.Id, SpaceId = row.SpaceId })
            .HasCommand("Merge").Where<MultipleSharedLinkRequest>((row, request) => row.Zone == request.Zone && row.SpaceId == request.SpaceId && row.Id == request.OwnerId)
            .ReconcileLinks<MultipleSharedLinkRow, MultipleSharedLinkItem, (int, string, int), MultipleSharedLinkRequest>(row => row.Links, request => request.Items,
                row => new ValueTuple<int, string, int>(row.Scope1, row.TargetCode, row.Scope2), item => new ValueTuple<int, string, int>(item.Scope1, item.Code, item.Scope2),
                links => links.AllowSharedReferenceColumns().MaximumItems(3).Add(item => new MultipleSharedLinkRow { Scope1 = item.Scope1, Scope2 = item.Scope2, TargetCode = item.Code, Caption = "new" }))
            .Save().Returns(row => row.Id).SimulateCollectionWrites();
        return builder;
    }
}
