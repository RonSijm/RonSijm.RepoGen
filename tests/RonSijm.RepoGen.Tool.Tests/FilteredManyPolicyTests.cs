using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.FilteredManyTestHarness;
using static RonSijm.RepoGen.Tool.Tests.ManyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredManyPolicyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BoundEndpointAndJoinFiltersResolveOneTypedKeyAndNeverConstructAFixtureContext(bool mapped)
    {
        using var context = new FilteredManyKeyContext(new DbContextOptionsBuilder<ManyReadContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = FilteredManyTestHarness.Builder(mapped);
        var owner = builder.Entity<ManyReadOwner>(); var tag = builder.Entity<ManyReadTag>();
        var definition = builder.Build().Entities.Single(entity => entity.EntityType == typeof(ManyReadOwner));
        foreach (var query in definition.Queries.ToArray()) owner.ExcludeQuery(query.MethodName);
        foreach (var command in definition.Commands.ToArray()) owner.ExcludeCommand(command.MethodName);
        tag.ExcludeQuery("Owners");
        builder.Configure(options => options.UseKeyedContextLeases<ContextFilterKey>());
        builder.HasInMemoryModelFilterContext<FilteredManyKeyContext, ContextFilterKey, int>(ctx => ctx.TenantId, key => key.FolderId);
        owner.HasQuery("Cards").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key).OrderBy(row => row.Id)
            .Select(row => new ManyReadCard(row.Id, row.Name, row.Tags.OrderBy(item => item.Id).Select(item => item.Name).ToList())).List();
        owner.HasCommand("Rename").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key)
            .Where<ContextFilterRequest>((row, input) => row.Id == input.Id)
            .Patch(setters => setters.SetProperty<string, ContextFilterRequest>(row => row.Name, (row, input) => input.Title))
            .UseInMemoryModelQueryFilter(includeNavigations: true).Save()
            .ReturnsSaved(row => new ManyReadCard(row.Id, row.Name, row.Tags.OrderBy(item => item.Id).Select(item => item.Name).ToList()));
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle); SavedResultTestHarness.CompileProduction(context, builder.Build());
        const string ns = "Generated.ManyRead.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "FilteredManyKeyContextRepositoryFixture")!)!;
        var repo = Activator.CreateInstance(assembly.GetType(ns + "ManyReadOwnerRepositoryTestDouble")!, fixture)!;
        var key = new ContextFilterKey("first", FolderId: 1);
        var secondKey = new ContextFilterKey("second", FolderId: 2);
        var data = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, [key])!; Seed(data, mapped);
        var second = (InMemoryWriteDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, [secondKey])!; Seed(second, mapped);
        var input = new ContextFilterRequest(key);
        var cards = await Invoke<Task<IReadOnlyList<ManyReadCard>>>(repo, "CardsAsync", input);
        input.KeyReads.Should().Be(1); cards.Select(row => row.Id).Should().Equal(1, 3, 4); cards[0].Tags.Should().Equal("Red", "Private tag");
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(repo, "CardsAsync", new ContextFilterRequest(secondKey))).Single().Id.Should().Be(2);
        var write = new ContextFilterRequest(key, 1, "keyed");
        (await Invoke<Task<CommandResult<ManyReadCard>>>(repo, "RenameAsync", write)).Value!.Tags.Should().Equal("Red", "Private tag");
        write.KeyReads.Should().Be(1);
        (await Invoke<Task<CommandResult<ManyReadCard>>>(repo, "RenameAsync", new ContextFilterRequest(secondKey, 1))).Status.Should().Be(CommandStatus.NotFound);
        data.Should().NotBeSameAs(second);
    }

    [Theory]
    [InlineData("missingOwner")]
    [InlineData("missingTarget")]
    [InlineData("duplicateHiddenPair")]
    public async Task VisibleLinksStillValidateHiddenPrincipalsAndScriptsCanOverrideInvalidArrangements(string mode)
    {
        var (repo, _, data) = Fixture();
        LinkStore(data).Seed(mode switch
        {
            "missingOwner" => new(99, 1, "missing", 1, false),
            "missingTarget" => new(99, 1, "same", 99, false),
            _ => new(99, 2, "same", 2, false)
        });
        Func<Task> read = async () => await Invoke<Task<IReadOnlyList<ManyReadCard>>>(repo, "CardsAsync");
        await read.Should().ThrowAsync<InvalidOperationException>();
        ScriptedTestDoubleTestHarness.Returns(repo, "OnCardsAsync", (IReadOnlyList<ManyReadCard>)[new(99, "scripted", [])]);
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(repo, "CardsAsync")).Single().Id.Should().Be(99);
        repo.GetType().GetMethod("ResetTestDouble")!.Invoke(repo, null);
        await read.Should().ThrowAsync<InvalidOperationException>(); data.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task FilteredOutBadJoinRowsDoNotParticipateButPostSaveVisibleFailuresDoNotUndoWrites()
    {
        var (repo, _, data) = Fixture();
        LinkStore(data).Seed(new(99, 1, "missing", 99, true));
        (await Invoke<Task<IReadOnlyList<ManyReadCard>>>(repo, "CardsAsync")).Should().HaveCount(3);
        LinkStore(data).Remove(99); LinkStore(data).Seed(new(99, 1, "same", 99, false));
        Func<Task> save = async () => await Invoke<Task<CommandResult<ManyReadCard>>>(repo, "RenameAsync", 1);
        await save.Should().ThrowAsync<RepositorySavedResultException>();
        OwnerStore(data).Find((1, 1))!.Name.Should().Be("renamed"); data.PendingCount.Should().Be(0);
    }
}
