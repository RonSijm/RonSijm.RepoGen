using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.EntityFrameworkCore.Tests;

public sealed class SelectorIntegrationTests
{
    [Fact]
    public async Task SingleSelectorIsLazyAndProjectsOnlyRequestedColumns()
    {
        await using var store = await SqliteTestStore.CreateAsync();

        var selector = store.Repository.ByEmail("ron@example.com");

        store.Interceptor.Commands.Should().BeEmpty();
        var result = await selector.AsSummaryDtoAsync();

        result.Should().NotBeNull();
        result!.Name.Should().Be("Ron");
        store.Interceptor.Commands.Should().ContainSingle();
        var selectClause = GetSelectClause(store.Interceptor.Commands.Single());
        selectClause.Should().Contain("Id");
        selectClause.Should().Contain("Name");
        selectClause.Should().NotContain("Email");
        selectClause.Should().NotContain("DateOfBirth");
        selectClause.Should().NotContain("PracticeId");
    }

    [Fact]
    public async Task SingleSelectorReturnsNullWhenNoRowMatches()
    {
        await using var store = await SqliteTestStore.CreateAsync();

        var result = await store.Repository
            .ByEmail("missing@example.com")
            .AsReportDtoAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task CollectionSelectorProjectsAndSupportsTerminalOperations()
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var selector = store.Repository.ByPracticeId(store.PracticeId);

        var reports = await selector.AsReportDtosAsync();
        var count = await selector.CountAsync();
        var exists = await selector.ExistsAsync();

        reports.Should().HaveCount(2);
        reports.Select(static report => report.Name).Should().BeEquivalentTo("Ron", "Ada");
        count.Should().Be(2);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task CollectionSelectorSupportsQueryShapingAdHocProjectionAndAggregates()
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var selector = store.Repository.GetAll();

        var page = await selector
            .Where(static patient => patient.Score > 0)
            .OrderByDescending(static patient => patient.Name)
            .Skip(1)
            .Take(1)
            .ProjectToListAsync(static patient => new { patient.Name, patient.Score });
        var maximum = await selector.MaxAsync(static patient => patient.Score);
        var minimum = await selector.MinAsync(static patient => patient.Score);
        var sum = await selector.SumAsync(static patient => patient.Score);
        var average = await selector.AverageAsync(static patient => patient.Score);

        page.Should().ContainSingle().Which.Name.Should().Be("Ada");
        maximum.Should().Be(11);
        minimum.Should().Be(7);
        sum.Should().Be(18);
        average.Should().Be(9);
    }

    [Fact]
    public async Task CollectionSelectorSupportsGroupedAggregateProjections()
    {
        await using var store = await SqliteTestStore.CreateAsync();

        var groups = await store.Repository
            .GetAll()
            .GroupBy(static patient => patient.PracticeId)
            .ProjectToListAsync(static group => new
            {
                PracticeId = group.Key,
                Count = group.Count(),
                TotalScore = group.Sum(static patient => patient.Score),
                MaximumScore = group.Max(static patient => patient.Score)
            });

        groups.Should().ContainSingle();
        groups[0].PracticeId.Should().Be(store.PracticeId);
        groups[0].Count.Should().Be(2);
        groups[0].TotalScore.Should().Be(18);
        groups[0].MaximumScore.Should().Be(11);
    }

    [Fact]
    public async Task CollectionSelectorReturnsProjectedPagedResult()
    {
        await using var store = await SqliteTestStore.CreateAsync();

        var result = await store.Repository
            .GetAll()
            .OrderBy(static patient => patient.Name)
            .ToPagedListAsync(
                static patient => new { patient.Name, patient.Score },
                page: 2,
                pageSize: 1);

        result.TotalCount.Should().Be(2);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(1);
        result.Items.Should().ContainSingle();
        result.Items[0].Name.Should().Be("Ron");
        result.Items[0].Score.Should().Be(7);
    }

    [Fact]
    public async Task CollectionSelectorReturnsProjectedCursorPages()
    {
        await using var store = await SqliteTestStore.CreateAsync();

        var first = await store.Repository
            .GetAll()
            .OrderBy(static patient => patient.Score)
            .ToCursorPagedListAsync(
                static patient => patient.Name,
                static patient => patient.Score,
                pageSize: 1);

        first.Items.Should().ContainSingle().Which.Should().Be("Ron");
        first.HasMore.Should().BeTrue();
        first.NextCursor.Should().Be(7);

        var second = await store.Repository
            .GetAll()
            .Where(patient => patient.Score > first.NextCursor)
            .OrderBy(static patient => patient.Score)
            .ToCursorPagedListAsync(
                static patient => patient.Name,
                static patient => patient.Score,
                pageSize: 1);

        second.Items.Should().ContainSingle().Which.Should().Be("Ada");
        second.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task CollectionSelectorStreamsProjectedResults()
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var names = new List<string>();

        await foreach (var name in store.Repository
                           .GetAll()
                           .OrderBy(static patient => patient.Name)
                           .StreamAsync(static patient => patient.Name))
        {
            names.Add(name);
        }

        names.Should().Equal("Ada", "Ron");
    }

    [Fact]
    public async Task CollectionSelectorSupportsDirectFirstAndDictionaryTerminals()
    {
        await using var store = await SqliteTestStore.CreateAsync();

        var highestScoring = await store.Repository
            .GetAll()
            .OrderByDescending(static patient => patient.Score)
            .FirstOrDefaultAsync();
        var namesByEmail = await store.Repository
            .GetAll()
            .ToDictionaryAsync(static patient => patient.Email, static patient => patient.Name);

        highestScoring.Should().NotBeNull();
        highestScoring!.Name.Should().Be("Ada");
        namesByEmail.Should().Contain("ron@example.com", "Ron");
        namesByEmail.Should().Contain("ada@example.com", "Ada");
    }

    [Fact]
    public async Task EntityMaterializationPreservesNormalTrackingBehavior()
    {
        await using var store = await SqliteTestStore.CreateAsync();

        var patient = await store.Repository
            .ByEmail("ron@example.com")
            .AsEntityAsync();

        patient.Should().NotBeNull();
        store.Context.Entry(patient!).State.Should().Be(Microsoft.EntityFrameworkCore.EntityState.Unchanged);
    }

    [Fact]
    public async Task SelectorsCanOptOutOfTracking()
    {
        await using var store = await SqliteTestStore.CreateAsync();

        var patient = await store.Repository
            .ByEmail("ron@example.com")
            .AsNoTracking()
            .AsEntityAsync();
        var patients = await store.Repository
            .GetAll()
            .AsNoTracking()
            .AsEntitiesAsync();

        patient.Should().NotBeNull();
        patients.Should().HaveCount(2);
        store.Context.Entry(patient!).State.Should().Be(Microsoft.EntityFrameworkCore.EntityState.Detached);
        patients.Should().OnlyContain(item =>
            store.Context.Entry(item).State == Microsoft.EntityFrameworkCore.EntityState.Detached);
    }

    [Fact]
    public async Task SingleSelectorCanUpdateAndSaveFluently()
    {
        await using var store = await SqliteTestStore.CreateAsync();

        var patient = await store.Repository
            .ByEmail("ron@example.com")
            .AsNoTracking()
            .Update(static entity => entity.Name = "Ronald")
            .SaveChangesAsync();

        patient.Should().NotBeNull();
        patient!.Name.Should().Be("Ronald");
        store.Context.Entry(patient).State.Should().Be(Microsoft.EntityFrameworkCore.EntityState.Unchanged);
        store.Context.ChangeTracker.Clear();
        (await store.Context.Patients.SingleAsync(entity => entity.Email == "ron@example.com"))
            .Name.Should().Be("Ronald");
    }

    [Fact]
    public async Task SingleSelectorCanDeleteAndSaveFluently()
    {
        await using var store = await SqliteTestStore.CreateAsync();

        var deleted = await store.Repository
            .ByEmail("ron@example.com")
            .Delete()
            .SaveChangesAsync();

        deleted.Should().BeTrue();
        (await store.Context.Patients.AnyAsync(entity => entity.Email == "ron@example.com"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task SingleSelectorDistinguishesStagedAndImmediateMutations()
    {
        await using var store = await SqliteTestStore.CreateAsync();

        var stagedUpdate = await store.Repository
            .ByEmail("ron@example.com")
            .StageUpdateAsync(static patient => patient.Name = "Ronald");
        var stagedDelete = await store.Repository
            .ByEmail("ada@example.com")
            .MarkForDeletionAsync();

        stagedUpdate.Should().NotBeNull();
        stagedDelete.Should().BeTrue();
        (await store.Context.Patients.AsNoTracking().SingleAsync(patient => patient.Email == "ron@example.com"))
            .Name.Should().Be("Ron");
        (await store.Context.Patients.AsNoTracking().AnyAsync(patient => patient.Email == "ada@example.com"))
            .Should().BeTrue();

        await store.Context.SaveChangesAsync();
        store.Context.ChangeTracker.Clear();

        (await store.Context.Patients.AsNoTracking().SingleAsync(patient => patient.Email == "ron@example.com"))
            .Name.Should().Be("Ronald");
        (await store.Context.Patients.AsNoTracking().AnyAsync(patient => patient.Email == "ada@example.com"))
            .Should().BeFalse();

        var updatedImmediately = await store.Repository
            .ByEmail("ron@example.com")
            .UpdateAsync(static patient => patient.Name = "Ron Immediate");

        updatedImmediately.Should().NotBeNull();
        (await store.Context.Patients.AsNoTracking().SingleAsync(patient => patient.Email == "ron@example.com"))
            .Name.Should().Be("Ron Immediate");

        var deletedImmediately = await store.Repository
            .ByEmail("ron@example.com")
            .DeleteAsync();

        deletedImmediately.Should().BeTrue();
        (await store.Context.Patients.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task FluentWriteInvokesConfiguredCacheInvalidator()
    {
        await using var store = await SqliteTestStore.CreateAsync();
        var invalidationCount = 0;
        var selector = new PatientSelector(
            store.Context,
            store.Context.Patients.Where(static patient => patient.Email == "ron@example.com"),
            _ =>
            {
                invalidationCount++;
                return Task.CompletedTask;
            });

        var result = await selector
            .AsNoTracking()
            .Where(static patient => patient.Score > 0)
            .Update(static patient => patient.Name = "Ronald")
            .SaveChangesAsync();

        result.Should().NotBeNull();
        invalidationCount.Should().Be(1);
    }

    [Fact]
    public void SelectorDoesNotExposeQueryOrConstructorPublicly()
    {
        typeof(PatientSelector)
            .GetProperty("Query", BindingFlags.Instance | BindingFlags.Public)
            .Should()
            .BeNull();
        typeof(PatientSelector)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .Should()
            .BeEmpty();
    }

    private static string GetSelectClause(string sql)
    {
        var fromIndex = sql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
        fromIndex.Should().BePositive();
        return sql.Substring(0, fromIndex);
    }
}