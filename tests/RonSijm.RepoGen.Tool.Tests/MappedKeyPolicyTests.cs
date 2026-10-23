using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryQueryTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedKeyPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrincipalComparisonRemainsSeparateFromBoundKeyValues(bool comparer)
    {
        using var context = MappedKeyTestHarness.Context();
        var builder = MappedKeyTestHarness.Builder();
        if (comparer)
        {
            builder.Entity<MappedKeyOwner>().HasInMemoryRelationshipComparer(row => row.Code, () => StringComparer.OrdinalIgnoreCase);
        }
        var (owner, child, _, data) = MappedKeyTestHarness.Fixture(ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build())));
        MappedKeyTestHarness.Store<MappedKeyChildSeed>(data, nameof(MappedKeyChild)).Seed(new(1, 7, "A", "first"), InMemoryDuplicateKeyBehavior.Replace);
        MappedKeyTestHarness.Store<MappedKeyLinkSeed>(data, nameof(MappedKeyLink)).Seed(new(1, 7, "A", 2), InMemoryDuplicateKeyBehavior.Replace);
        var cards = await Invoke<Task<IReadOnlyList<MappedKeyCard>>>(owner, "CardsAsync");
        cards[0].Children.Should().HaveCount(comparer ? 1 : 0);
        cards[0].Tags.Should().HaveCount(comparer ? 1 : 0);
        (await Invoke<Task<IReadOnlyList<MappedKeyChildCard>>>(child, "CardsAsync"))[0].Owner.Should().Be(comparer ? "one" : "none");
    }

    [Fact]
    public void ExplicitPropertyValuesDoNotAcknowledgeProviderKeyConverters()
    {
        using var context = new MappedKeyConvertedContext(new DbContextOptionsBuilder<MappedKeyContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = MappedKeyTestHarness.Base();
        builder.Entity<MappedKeyOwner>().HasQuery("Read").Select(row => row.Children.Count).List();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*custom key conversion/comparison*");
    }

    [Fact]
    public async Task NullPreservationStillRequiresItsOwnNavigationPermissionAndSafeProjection()
    {
        using var context = MappedKeyTestHarness.Context();
        var builder = MappedKeyTestHarness.Base();
        var owner = builder.Entity<MappedKeyOwner>();
        owner.HasInMemoryNavigation(row => row.Tags, preserveMissingTargets: true, useMappedPropertyKeys: true);
        owner.HasQuery("Nulls").OrderBy(row => row.Id).Select(row => row.Tags.Count(tag => tag == null)).List();
        var (repository, _, _, _) = MappedKeyTestHarness.Fixture(ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build())));
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "NullsAsync")).Should().Equal(1, 0);
    }

    [Fact]
    public async Task MissingPermissionThrowsOnEmptyFixturesAndTypedScriptsStillWin()
    {
        using var context = MappedKeyTestHarness.Context();
        var builder = MappedKeyTestHarness.Base(permission: false, strict: false);
        builder.Entity<MappedKeyOwner>().HasQuery("Read").Select(row => row.Children.Count).List();
        var (repository, _, _, data) = MappedKeyTestHarness.Fixture(ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build())));
        data.Clear();
        Func<Task> read = async () => await Invoke<Task<IReadOnlyList<int>>>(repository, "ReadAsync");
        await read.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        ScriptedTestDoubleTestHarness.Returns(repository, "OnReadAsync", new List<int> { 42 });
        (await Invoke<Task<IReadOnlyList<int>>>(repository, "ReadAsync")).Should().Equal(42);
    }
}
