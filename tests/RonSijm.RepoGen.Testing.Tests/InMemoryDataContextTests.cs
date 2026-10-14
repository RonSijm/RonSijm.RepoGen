using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryDataContextTests
{
    [Fact]
    public void InitialBaselineIsEmptyAndResetPreservesStoreInstances()
    {
        var context = new InMemoryDataContext();
        var first = context.CreateStore<FixtureRow, int>("first", row => row.Id, FixtureRow.Copy);
        var second = context.CreateStore<FixtureRow, int>("second", row => row.Id, FixtureRow.Copy);
        first.Seed(new() { Id = 1 });
        second.Seed(new() { Id = 2 });
        context.RestoreBaseline();
        first.Count.Should().Be(0);
        second.Count.Should().Be(0);
        first.Seed(new() { Id = 1, Tags = ["baseline"] });
        context.CaptureBaseline();
        context.Clear();
        context.RestoreBaseline();
        first.Find(1)!.Tags.Add("inspection");
        first.Seed(new() { Id = 1, Tags = ["replacement"] }, InMemoryDuplicateKeyBehavior.Replace);
        context.RestoreBaseline();
        first.Find(1)!.Tags.Should().Equal("baseline");
        second.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedBaselinePreparationNeverPartiallyChangesAnyStore(bool capturing)
    {
        var fail = false;
        var context = new InMemoryDataContext();
        var first = context.CreateStore<FixtureRow, int>("first", row => row.Id, FixtureRow.Copy);
        var second = context.CreateStore<FixtureRow, int>("second", row => row.Id,
            row => fail ? throw new InvalidOperationException("adapter failure") : FixtureRow.Copy(row));
        first.Seed(new() { Id = 1, Name = "baseline first" });
        second.Seed(new() { Id = 2, Name = "baseline second" });
        context.CaptureBaseline();
        first.ReplaceWith([new() { Id = 3, Name = "current first" }]);
        second.ReplaceWith([new() { Id = 4, Name = "current second" }]);
        fail = true;
        Action action = capturing ? context.CaptureBaseline : context.RestoreBaseline;
        action.Should().Throw<InvalidOperationException>().WithMessage("adapter failure");
        first.Find(3)!.Name.Should().Be("current first");
        second.Count.Should().Be(1);
        fail = false;
        second.Find(4)!.Name.Should().Be("current second");
        context.RestoreBaseline();
        first.Find(1)!.Name.Should().Be("baseline first");
        second.Find(2)!.Name.Should().Be("baseline second");
    }

    [Fact]
    public void StoresRegisteredAfterCaptureHaveAnEmptyBaseline()
    {
        var context = new InMemoryDataContext();
        var first = context.CreateStore<FixtureRow, int>("first", row => row.Id, FixtureRow.Copy);
        first.Seed(new() { Id = 1 });
        context.CaptureBaseline();
        var later = context.CreateStore<FixtureRow, int>("later", row => row.Id, FixtureRow.Copy);
        later.Seed(new() { Id = 2 });
        context.RestoreBaseline();
        first.Count.Should().Be(1);
        later.Count.Should().Be(0);
    }

    [Fact]
    public void StoreNamesAreUniqueAndAdaptersAreRequired()
    {
        var context = new InMemoryDataContext();
        context.CreateStore<FixtureRow, int>("first", row => row.Id, FixtureRow.Copy);
        Action duplicate = () => context.CreateStore<FixtureRow, string>("first", row => row.Name, FixtureRow.Copy);
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*already registered*");
        Action blank = () => context.CreateStore<FixtureRow, int>(" ", row => row.Id, FixtureRow.Copy);
        blank.Should().Throw<ArgumentException>();
        Action noKey = () => context.CreateStore<FixtureRow, int>("second", null!, FixtureRow.Copy);
        noKey.Should().Throw<ArgumentNullException>();
        Action noCopy = () => context.CreateStore<FixtureRow, int>("second", row => row.Id, null!);
        noCopy.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReentrantAdapterOrEnumeratorCannotMutateTheContext(bool iterator)
    {
        var context = new InMemoryDataContext();
        var reenter = false;
        var store = context.CreateStore<FixtureRow, int>("rows", row => row.Id, row =>
        {
            if (reenter) context.Clear();
            return FixtureRow.Copy(row);
        });
        store.Seed(new() { Id = 1 });
        reenter = !iterator;
        Action seed = () => store.SeedRange(iterator ? ReentrantRows() : [new() { Id = 2 }]);
        seed.Should().Throw<InvalidOperationException>().WithMessage("*cannot re-enter*");
        store.Count.Should().Be(1);
        reenter = false;
        store.Snapshot().Should().ContainSingle().Which.Id.Should().Be(1);

        IEnumerable<FixtureRow> ReentrantRows()
        {
            yield return new() { Id = 2 };
            context.Clear();
        }
    }

    [Fact]
    public void RuntimeRemainsIndependentOfModelsDesignersAndEntityFramework()
        => typeof(InMemoryDataContext).Assembly.GetReferencedAssemblies().Should().NotContain(name =>
            name.Name!.Contains("EntityFramework", StringComparison.Ordinal) || name.Name.Contains("RepoGen.Design", StringComparison.Ordinal));
}
