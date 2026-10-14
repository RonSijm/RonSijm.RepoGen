using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

#pragma warning disable EF1002 // Fixture identifiers are a fixed prefix + GUID + fixed suffix; never caller input.

public sealed class SqlServerQueryCoverageTests
{
    [SqlServerFixtureFact]
    public async Task CompositeLeftAndInnerJoinsPreserveMissingRowsAndAntiJoinsUseServerExists()
    {
        await using var fixture = await SqlQueryFixture.CreateAsync();
        await using var context = fixture.Context();
        var builder = Builder();
        builder.Entity<SqlQueryItem>(entity =>
        {
            entity.HasQuery("Inner").From(sources => from item in sources.Set<SqlQueryItem>()
                                                     join owner in sources.Set<SqlQueryOwner>() on new { Id = item.OwnerId, item.Region } equals new { Id = (int?)owner.Id, owner.Region }
                                                     orderby item.Id
                                                     select item.Id).List();
            entity.HasQuery("Left").From(sources => from item in sources.Set<SqlQueryItem>()
                                                    join owner in sources.Set<SqlQueryOwner>() on new { Id = item.OwnerId, item.Region } equals new { Id = (int?)owner.Id, owner.Region } into owners
                                                    from owner in owners.DefaultIfEmpty()
                                                    orderby item.Id
                                                    select new SqlQuerySummary(item.Id, owner == null ? null : owner.Name)).List();
            entity.HasQuery("Anti").From(sources => sources.Set<SqlQueryItem>().Where(item => !sources.Set<SqlQueryOwner>()
                .Any(owner => (int?)owner.Id == item.OwnerId && owner.Region == item.Region)).OrderBy(item => item.Id).Select(item => item.Id)).List();
            entity.HasQuery("Exists").From(sources => sources.Set<SqlQueryItem>().Where(item => sources.Set<SqlQueryOwner>()
                .Any(owner => (int?)owner.Id == item.OwnerId && owner.Region == item.Region)).OrderBy(item => item.Id).Select(item => item.Id)).List();
        });
        var operations = Operations(context, builder);
        (await Call<Task<IReadOnlyList<int>>>(operations, "InnerAsync", CancellationToken.None)).Should().Equal(1, 6);
        var left = await Call<Task<IReadOnlyList<SqlQuerySummary>>>(operations, "LeftAsync", CancellationToken.None);
        left.Select(row => row.Id).Should().Equal(1, 2, 3, 5, 6);
        left.Where(row => row.OwnerName is null).Select(row => row.Id).Should().Equal(2, 3, 5);
        (await Call<Task<IReadOnlyList<int>>>(operations, "AntiAsync", CancellationToken.None)).Should().Equal(2, 3, 5);
        (await Call<Task<IReadOnlyList<int>>>(operations, "ExistsAsync", CancellationToken.None)).Should().Equal(1, 6);
        fixture.Sql.Commands.Should().HaveCount(4);
        fixture.Sql.Commands[0].Text.Should().Contain("INNER JOIN");
        fixture.Sql.Commands[1].Text.Should().Contain("LEFT JOIN");
        fixture.Sql.Commands[2].Text.Should().Contain("NOT EXISTS");
        fixture.Sql.Commands[3].Text.Should().Contain("EXISTS");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [SqlServerFixtureFact]
    public async Task SetOperatorsKeepTheirDuplicateSemanticsAndExecuteOneSqlStatementEach()
    {
        await using var fixture = await SqlQueryFixture.CreateAsync();
        await using var context = fixture.Context();
        var builder = Builder();
        builder.Entity<SqlQueryItem>(entity =>
        {
            entity.HasQuery("Union").From(sources => sources.Set<SqlQueryItem>().Where(item => item.Id <= 2).Select(item => item.Id)
                .Union(sources.Set<SqlQueryItem>().Where(item => item.Id >= 2 && item.Id <= 3).Select(item => item.Id)).OrderBy(id => id)).List();
            entity.HasQuery("Concat").From(sources => sources.Set<SqlQueryItem>().Where(item => item.Id <= 2).Select(item => item.Id)
                .Concat(sources.Set<SqlQueryItem>().Where(item => item.Id >= 2 && item.Id <= 3).Select(item => item.Id)).OrderBy(id => id)).List();
            entity.HasQuery("Except").From(sources => sources.Set<SqlQueryItem>().Where(item => item.Id <= 3).Select(item => item.Id)
                .Except(sources.Set<SqlQueryItem>().Where(item => item.Id >= 2).Select(item => item.Id))).List();
            entity.HasQuery("Intersect").From(sources => sources.Set<SqlQueryItem>().Where(item => item.Id <= 3).Select(item => item.Id)
                .Intersect(sources.Set<SqlQueryItem>().Where(item => item.Id >= 2).Select(item => item.Id)).OrderBy(id => id)).List();
        });
        var operations = Operations(context, builder);
        (await Call<Task<IReadOnlyList<int>>>(operations, "UnionAsync", CancellationToken.None)).Should().Equal(1, 2, 3);
        (await Call<Task<IReadOnlyList<int>>>(operations, "ConcatAsync", CancellationToken.None)).Should().Equal(1, 2, 2, 3);
        (await Call<Task<IReadOnlyList<int>>>(operations, "ExceptAsync", CancellationToken.None)).Should().Equal(1);
        (await Call<Task<IReadOnlyList<int>>>(operations, "IntersectAsync", CancellationToken.None)).Should().Equal(2, 3);
        fixture.Sql.Commands.Select(command => command.Text).Should().HaveCount(4);
        fixture.Sql.Commands[0].Text.Should().Contain("UNION").And.NotContain("UNION ALL");
        fixture.Sql.Commands[1].Text.Should().Contain("UNION ALL");
        fixture.Sql.Commands[2].Text.Should().Contain("EXCEPT");
        fixture.Sql.Commands[3].Text.Should().Contain("INTERSECT");
    }

    [SqlServerFixtureFact]
    public async Task JoinedGroupingHavingAndCountedPagesRemainSqlComposedAndDictionaryColumnsArePruned()
    {
        await using var fixture = await SqlQueryFixture.CreateAsync();
        await using var context = fixture.Context();
        var builder = Builder();
        builder.Entity<SqlQueryItem>(entity =>
        {
            entity.HasQuery("Totals").From((RepositoryQuerySources sources, SqlQueryRequest request) =>
                    from item in sources.Set<SqlQueryItem>()
                    join line in sources.Set<SqlQueryLine>() on item.Id equals line.ItemId
                    where line.Quantity >= request.MinimumQuantity
                    select new { item.Region, line.Quantity })
                .GroupBy(row => row.Region, group => new SqlQueryTotal { Region = group.Key, Count = group.Count(), Total = group.Sum(row => row.Quantity) })
                .Where<SqlQueryRequest>((total, request) => total.Count >= request.MinimumCount).OrderBy(total => total.Region).RequirePagingOrder().Paged();
            entity.HasQuery("Dictionary").From(sources => from item in sources.Set<SqlQueryItem>()
                                                          join owner in sources.Set<SqlQueryOwner>() on item.OwnerId equals (int?)owner.Id
                                                          select new { item.Id, owner.Name, item.Payload }).Dictionary(row => row.Id, row => row.Name);
        });
        var operations = Operations(context, builder);
        var page = await Call<Task<PagedResult<SqlQueryTotal>>>(operations, "TotalsAsync", new SqlQueryRequest(1, 4, 10, 10, 2), 1, 1, CancellationToken.None);
        page.TotalCount.Should().Be(1);
        page.Items.Single().Region.Should().Be(7);
        page.Items.Single().Count.Should().Be(2);
        page.Items.Single().Total.Should().Be(14);
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands.Should().OnlyContain(command => command.Text.Contains("HAVING", StringComparison.Ordinal));
        fixture.Sql.Commands[1].Text.Should().Contain("OFFSET").And.Contain("FETCH");
        fixture.Sql.Commands.Should().OnlyContain(command => command.Values.Length > 0);
        fixture.Sql.Commands.Clear();
        var dictionary = await Call<Task<IReadOnlyDictionary<int, string>>>(operations, "DictionaryAsync", CancellationToken.None);
        dictionary.Keys.Should().HaveCount(3).And.Contain(1).And.Contain(5).And.Contain(6);
        fixture.Sql.Commands.Single().Text.Should().NotContain("Payload");
    }

    [SqlServerFixtureFact]
    public async Task RequestFilteredNestedLoadingHasDeliberateSingleSplitAndPagedRoundTrips()
    {
        await using var fixture = await SqlQueryFixture.CreateAsync();
        await using var context = fixture.Context();
        var operations = Operations(context, LoadingBuilder());
        var request = new SqlQueryRequest(1, 4, 10, 1, 0);
        var split = await Call<Task<IReadOnlyList<SqlQueryDetail>>>(operations, "SplitAsync", request, CancellationToken.None);
        split.Should().ContainSingle();
        split[0].OwnerName.Should().Be("Ada");
        split[0].Lines.Select(line => line.Quantity).Should().Equal(5, 9);
        split[0].Lines.Should().OnlyContain(line => line.Notes.Count == 1);
        split[0].Lines.SelectMany(line => line.Notes).Should().OnlyContain(note => note == "public");
        fixture.Sql.Commands.Should().HaveCount(3); // Root + lines + nested notes.
        fixture.Sql.Commands.Should().Contain(command => command.Text.Contains("ROW_NUMBER", StringComparison.Ordinal));
        fixture.Sql.Commands.Clear();
        var single = await Call<Task<IReadOnlyList<SqlQueryDetail>>>(operations, "SingleAsync", request, CancellationToken.None);
        single.Should().BeEquivalentTo(split);
        fixture.Sql.Commands.Should().ContainSingle().Which.Text.Should().Contain("JOIN").And.Contain("ROW_NUMBER");
        fixture.Sql.Commands.Clear();
        var page = await Call<Task<PagedResult<SqlQueryDetail>>>(operations, "PageAsync", request, 1, 10, CancellationToken.None);
        page.TotalCount.Should().Be(1);
        page.Items.Should().BeEquivalentTo(split);
        fixture.Sql.Commands.Should().HaveCount(4);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [SqlServerFixtureFact]
    public async Task GlobalFiltersProtectOptionalNavigationButReviewedIgnoreFiltersDoesNotInjectRootPoliciesIntoIt()
    {
        await using var fixture = await SqlQueryFixture.CreateAsync();
        await using var context = fixture.Context();
        var builder = Builder();
        builder.Entity<SqlQueryItem>(entity =>
        {
            entity.HasQuery("Current").ReviewNavigationVisibility<SqlQueryOwner>("Live", "EF global filter protects the optional owner reference")
                .Where(item => item.Id == 2).Select(item => item.Owner == null ? null : item.Owner.Name).ResultNullability<string>(type => type.Nullable()).List();
            entity.HasQuery("History").ReviewNavigationVisibility<SqlQueryOwner>("Live", "Trusted historical read deliberately ignores global filters")
                .IgnoreQueryFilters().Where(item => item.Id == 2).Select(item => item.Owner == null ? null : item.Owner.Name).ResultNullability<string>(type => type.Nullable()).List();
        });
        var operations = Operations(context, builder);
        (await Call<Task<IReadOnlyList<string?>>>(operations, "CurrentAsync", CancellationToken.None)).Should().Equal((string?)null);
        (await Call<Task<IReadOnlyList<string?>>>(operations, "HistoryAsync", CancellationToken.None)).Should().Equal("Hidden");
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands[0].Text.Should().Contain("LEFT JOIN");
    }

    [SqlServerFixtureFact]
    public async Task ScalarDtoProjectionPrunesGraphIncludesAndOwnedStreamsDisposeOnEarlyExitAndCancellation()
    {
        await using var fixture = await SqlQueryFixture.CreateAsync();
        await using var context = fixture.Context();
        var builder = LoadingBuilder();
        builder.Configure(options => options.UseAsyncContextLeases());
        builder.Entity<SqlQueryItem>().HasQuery("Summary").Load(load => load.Include(item => item.Lines).ThenInclude(line => line.Notes))
            .OrderBy(item => item.Id).Select(item => new SqlQuerySummary(item.Id, item.Owner == null ? null : item.Owner.Name))
            .ReviewNavigationVisibility<SqlQueryOwner>("Live", "EF global filter protects the optional owner reference")
            .ReviewNavigationVisibility<SqlQueryLine>("LiveLines", "The summary prunes unused includes")
            .ReviewNavigationVisibility<SqlQueryNote>("PublicNotes", "The summary prunes unused includes").List();
        var assembly = Compile(context, builder);
        var provider = new SqlQueryLeaseProvider(fixture);
        var operations = Activator.CreateInstance(assembly.GetType(typeof(SqlQueryItem).Namespace + ".SqlQueryItemRepositoryOperations")!, provider)!;
        var summary = await Call<Task<IReadOnlyList<SqlQuerySummary>>>(operations, "SummaryAsync", CancellationToken.None);
        summary.Should().HaveCount(5);
        fixture.Sql.Commands.Should().ContainSingle().Which.Text.Should().NotContain("Payload").And.NotContain(fixture.Prefix + "Lines");
        provider.Contexts.Single().DisposeCount.Should().Be(1);
        var stream = Call<IAsyncEnumerable<SqlQueryDetail>>(operations, "StreamAsync", CancellationToken.None);
        provider.Contexts.Should().HaveCount(1);
        var enumerator = stream.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        var dto = enumerator.Current;
        dto.Lines.Should().HaveCount(3);
        provider.Contexts[1].DisposeCount.Should().Be(0);
        await enumerator.DisposeAsync();
        provider.Contexts[1].DisposeCount.Should().Be(1);
        dto.Lines[0].Notes.Should().NotBeEmpty();
        using var cancellation = new CancellationTokenSource();
        var canceled = Call<IAsyncEnumerable<SqlQueryDetail>>(operations, "StreamAsync", cancellation.Token).GetAsyncEnumerator();
        (await canceled.MoveNextAsync()).Should().BeTrue();
        await cancellation.CancelAsync();
        Func<Task> move = async () => { await canceled.MoveNextAsync(); };
        await move.Should().ThrowAsync<OperationCanceledException>();
        await canceled.DisposeAsync();
        provider.Contexts.Should().OnlyContain(acquired => acquired.DisposeCount == 1 && acquired.TrackedAtDisposal == 0);
    }

    [SqlServerFixtureFact]
    public async Task OptionalRankingLimitsAndGroupedCapsExecuteOnSqlServerWithoutClientFallback()
    {
        await using var fixture = await SqlQueryFixture.CreateAsync();
        await using var context = fixture.Context();
        context.Database.SetCommandTimeout(17);
        var builder = Builder();
        builder.Configure(options => options.UseCommandTimeout(3));
        var entity = builder.Entity<SqlQueryItem>();
        entity.HasQuery("Ranked").Results<SqlQueryItem>().OrderBy(item => item.Id)
            .OrderByDescendingIf((ConditionalRequest request) => request.Rank, (item, preferences) => item.Region + preferences.Bias)
            .ThenByDescendingIf((ConditionalRequest request) => request.Tie, item => item.Name).ThenBy(item => item.Id)
            .SkipIf(request => request.Skip, (ConditionalRequest input) => ConditionalCounts.Offset(input))
            .TakeIf(request => request.Take, (ConditionalRequest input) => ConditionalCounts.Limit(input))
            .Select(item => item.Id).RequirePagingOrder().Paged(RepositoryPageConsistency.Serializable);
        entity.HasQuery("Groups").Results<SqlQueryItem>().GroupBy(item => item.Region, group => new { Region = group.Key, Total = group.Sum(item => item.Id) })
            .OrderBy(group => group.Region).OrderByDescendingIf((ConditionalRequest request) => request.Rank, group => group.Total)
            .ThenBy(group => group.Region).TakeIf<ConditionalRequest>(request => request.Take, 1).Select(group => group.Region).RequirePagingOrder().Paged();
        var operations = Operations(context, builder);
        var plain = await Call<Task<PagedResult<int>>>(operations, "RankedAsync", new ConditionalRequest(), 1, 10, CancellationToken.None);
        plain.TotalCount.Should().Be(5);
        plain.Items.Should().Equal(1, 2, 3, 5, 6);
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands[^1].Text.Should().NotContain("Payload").And.NotContain("Name");
        fixture.Sql.Commands.Clear();
        var request = new ConditionalRequest { Rank = true, Tie = true, Bias = 17, Skip = true, Offset = 1, Take = true, Limit = 2 };
        var ranked = await Call<Task<PagedResult<int>>>(operations, "RankedAsync", request, 1, 10, CancellationToken.None);
        ranked.TotalCount.Should().Be(2);
        ranked.Items.Should().Equal(6, 3);
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands[^1].Text.Should().Contain("ORDER BY").And.Contain("DESC").And.Contain("OFFSET");
        context.Database.CurrentTransaction.Should().BeNull();
        context.Database.GetCommandTimeout().Should().Be(17);
        fixture.Sql.Commands.Clear();
        var groups = await Call<Task<PagedResult<int>>>(operations, "GroupsAsync", request, 1, 10, CancellationToken.None);
        groups.TotalCount.Should().Be(1);
        groups.Items.Should().Equal(8);
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands[^1].Text.Should().Contain("GROUP BY").And.Contain("SUM(").And.Contain("DESC");
        fixture.Sql.Commands.Clear();
        (await Call<Task<PagedResult<int>>>(operations, "GroupsAsync", new ConditionalRequest(), 1, 10, CancellationToken.None)).Items.Should().Equal(7, 8);
        context.ChangeTracker.Entries().Should().BeEmpty();
        context.Database.GetCommandTimeout().Should().Be(17);
    }

    [SqlServerFixtureFact]
    public async Task ExplicitDictionaryPoliciesRetainServerOrderingPruningGroupingAndVisibility()
    {
        await using var fixture = await SqlQueryFixture.CreateAsync();
        await using var context = fixture.Context();
        context.Database.SetCommandTimeout(17);
        var builder = Builder();
        builder.Configure(options => options.UseCommandTimeout(3));
        var entity = builder.Entity<SqlQueryItem>();
        foreach (var policy in Enum.GetValues<RepositoryDuplicateKeyBehavior>())
        {
            entity.HasQuery(policy.ToString()).Results<SqlQueryItem>().OrderBy(item => item.Id)
                .Dictionary(item => item.Region, item => item.Id, policy);
        }

        entity.HasQuery("CappedDictionary").Results<SqlQueryItem>().OrderBy(item => item.Id).Take(3)
            .Dictionary(item => item.Region, item => item.Id, RepositoryDuplicateKeyBehavior.KeepLast);
        entity.HasQuery("GroupDictionary").Results<SqlQueryItem>().GroupBy(item => item.Region, group => new { Region = group.Key, Count = group.Count() })
            .OrderBy(group => group.Region).Dictionary(group => 0, group => group.Count, RepositoryDuplicateKeyBehavior.KeepLast);
        var operations = Operations(context, builder);
        var first = await Call<Task<IReadOnlyDictionary<int, int>>>(operations, "KeepFirstAsync", CancellationToken.None);
        var last = await Call<Task<IReadOnlyDictionary<int, int>>>(operations, "KeepLastAsync", CancellationToken.None);
        first.Should().HaveCount(2);
        first[7].Should().Be(1);
        first[8].Should().Be(5);
        last.Should().HaveCount(2);
        last[7].Should().Be(3);
        last[8].Should().Be(6);
        fixture.Sql.Commands.Should().HaveCount(2).And.OnlyContain(command => command.Text.Contains("ORDER BY") && !command.Text.Contains("Payload") && !command.Text.Contains("Name"));
        (await Call<Task<IReadOnlyDictionary<int, int>>>(operations, "CappedDictionaryAsync", CancellationToken.None))[7].Should().Be(3);
        fixture.Sql.Commands[^1].Text.Should().Contain("TOP(");
        (await Call<Task<IReadOnlyDictionary<int, int>>>(operations, "GroupDictionaryAsync", CancellationToken.None))[0].Should().Be(2);
        fixture.Sql.Commands[^1].Text.Should().Contain("GROUP BY").And.Contain("COUNT(");
        Func<Task> duplicates = async () => await Call<Task<IReadOnlyDictionary<int, int>>>(operations, "ThrowAsync", CancellationToken.None);
        await duplicates.Should().ThrowAsync<ArgumentException>();
        fixture.Sql.Commands.Should().HaveCount(5);
        context.Database.GetCommandTimeout().Should().Be(17);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [SqlServerFixtureFact]
    public async Task WhitelistedResultSortingAndGroupedFieldsExecuteOnSqlServerWithPrunedColumns()
    {
        await using var fixture = await SqlQueryFixture.CreateAsync();
        await using var context = fixture.Context();
        context.Database.SetCommandTimeout(17);
        var builder = Builder();
        builder.Configure(options => options.UseCommandTimeout(3));
        var entity = builder.Entity<SqlQueryItem>();
        entity.HasQuery("Page").Select(item => new { item.Id, item.Region, item.Name }).OrderBy(item => item.Id)
            .OrderByField((FieldSortRequest request) => request.Field, preferences => preferences.Descending,
                fields => fields.Field("region", item => item.Region).Field("name", item => item.Name).PreserveOrderWhenUnspecified())
            .ThenBy(item => item.Id).Select(item => item.Id).RequirePagingOrder().Paged();
        entity.HasQuery("Groups").Results<SqlQueryItem>().GroupBy(item => item.Region, group => new { Region = group.Key, Count = group.Count(), Total = group.Sum(item => item.Id) })
            .OrderByField((FieldSortRequest request) => request.Field, request => request.Descending,
                fields => fields.Field("count", group => group.Count).Field("total", group => group.Total).UseDefault("count", true))
            .ThenBy(group => group.Region).Select(group => group.Region).RequirePagingOrder().Paged();
        var operations = Operations(context, builder);
        var plain = await Call<Task<PagedResult<int>>>(operations, "PageAsync", new FieldSortRequest(null), 1, 10, CancellationToken.None);
        plain.TotalCount.Should().Be(5);
        plain.Items.Should().Equal(1, 2, 3, 5, 6);
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands[^1].Text.Should().NotContain("Name").And.NotContain("Payload");
        fixture.Sql.Commands.Clear();
        (await Call<Task<PagedResult<int>>>(operations, "PageAsync", new FieldSortRequest("REGION", true), 1, 2, CancellationToken.None)).Items.Should().Equal(5, 6);
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands[^1].Text.Should().Contain("DESC").And.Contain("OFFSET").And.NotContain("Name").And.NotContain("Payload");
        fixture.Sql.Commands.Clear();
        (await Call<Task<PagedResult<int>>>(operations, "GroupsAsync", new FieldSortRequest(null), 1, 10, CancellationToken.None)).Items.Should().Equal(7, 8);
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands[^1].Text.Should().Contain("GROUP BY").And.Contain("COUNT(").And.Contain("DESC");
        fixture.Sql.Commands.Clear();
        (await Call<Task<PagedResult<int>>>(operations, "GroupsAsync", new FieldSortRequest("total", true), 1, 10, CancellationToken.None)).Items.Should().Equal(8, 7);
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands[^1].Text.Should().Contain("SUM(").And.Contain("DESC");
        fixture.Sql.Commands.Clear();
        Func<Task> invalid = async () => await Call<Task<PagedResult<int>>>(operations, "PageAsync", new FieldSortRequest("name; DROP TABLE Items"), 1, 10, CancellationToken.None);
        await invalid.Should().ThrowAsync<ArgumentException>();
        fixture.Sql.Commands.Should().BeEmpty();
        context.Database.GetCommandTimeout().Should().Be(17);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [SqlServerFixtureFact]
    public async Task RequestBoundTerminalsExecuteOnSqlServerWithTypedParametersAndPrunedColumns()
    {
        await using var fixture = await SqlQueryFixture.CreateAsync();
        await using var context = fixture.Context();
        context.Database.SetCommandTimeout(17);
        var builder = Builder();
        builder.Configure(options => options.UseCommandTimeout(3));
        var entity = builder.Entity<SqlQueryItem>();
        entity.HasQuery("Dictionary").Results<SqlQueryItem>().OrderBy(item => item.Id)
            .Dictionary((SqlQueryItem item, TerminalRequest request) => item.Id + request.Adjustment,
                (SqlQueryItem item, TerminalRequest format) => item.Name + format.Suffix);
        entity.HasQuery("Lookup").Results<SqlQueryItem>().OrderBy(item => item.Id)
            .Lookup((SqlQueryItem item, TerminalRequest request) => item.Region + request.Adjustment,
                (SqlQueryItem item, TerminalRequest format) => item.Name + format.Suffix);
        entity.HasQuery("Groups").Results<SqlQueryItem>().GroupBy(item => item.Region, group => new { Region = group.Key, Total = group.Sum(item => item.Id) })
            .Dictionary<int, int, TerminalRequest>((group, request) => group.Region + request.Adjustment, (group, format) => group.Total + format.Adjustment);
        entity.HasQuery("Sum").Select(item => item.Id).Sum<TerminalRequest>((id, request) => id + request.Adjustment);
        entity.HasQuery("Average").Select(item => item.Id).Average<TerminalRequest>((id, request) => (double)(id + request.Adjustment));
        entity.HasQuery("Maximum").Select(item => item.Id).Max<int, TerminalRequest>((id, request) => id + request.Adjustment);
        entity.HasQuery("Minimum").Select(item => item.Id).Min<int?, TerminalRequest>((id, request) => (int?)(id + request.Adjustment));
        entity.HasQuery("DecimalSum").Select(item => item.Id).Sum<TerminalRequest>((id, request) => (decimal)(id + request.Adjustment));
        entity.HasQuery("DecimalAverage").Select(item => item.Id).Average<TerminalRequest>((id, request) => (decimal)(id + request.Adjustment));
        entity.HasQuery("NullableDecimalSum").Select(item => item.Id).Sum<TerminalRequest>((id, request) => (decimal?)(id + request.Adjustment));
        entity.HasQuery("NullableDecimalAverage").Select(item => item.Id).Average<TerminalRequest>((id, request) => (decimal?)(id + request.Adjustment));
        var model = RepositoryModelFactory.Create(context, typeof(SqlQueryItem).Assembly, true, builder.Build());
        model.Entities.Single().Queries.Should().OnlyContain(query => query.Parameters.Count == 1);
        var operations = Operations(context, builder);
        var request = new TerminalRequest(3, "'; --", 0);
        var dictionary = await Call<Task<IReadOnlyDictionary<int, string>>>(operations, "DictionaryAsync", request, CancellationToken.None);
        dictionary.Keys.Should().Equal(4, 5, 6, 8, 9);
        dictionary[4].Should().Be("Alpha'; --");
        fixture.Sql.Commands.Single().Text.Should().NotContain("Payload").And.NotContain(request.Suffix);
        fixture.Sql.Commands.Single().Values.Should().Contain(request.Suffix);
        fixture.Sql.Commands.Clear();
        var lookup = await Call<Task<IReadOnlyDictionary<int, IReadOnlyList<string>>>>(operations, "LookupAsync", request, CancellationToken.None);
        lookup[10].Should().Equal("Alpha'; --", "Beta'; --", "Orphan'; --");
        var totals = await Call<Task<IReadOnlyDictionary<int, int>>>(operations, "GroupsAsync", request, CancellationToken.None);
        totals[10].Should().Be(9);
        totals[11].Should().Be(14);
        (await Call<Task<int>>(operations, "SumAsync", request, CancellationToken.None)).Should().Be(32);
        (await Call<Task<double>>(operations, "AverageAsync", request, CancellationToken.None)).Should().Be(6.4);
        (await Call<Task<int>>(operations, "MaximumAsync", request, CancellationToken.None)).Should().Be(9);
        (await Call<Task<int?>>(operations, "MinimumAsync", request, CancellationToken.None)).Should().Be(4);
        (await Call<Task<decimal>>(operations, "DecimalSumAsync", request, CancellationToken.None)).Should().Be(32m);
        (await Call<Task<decimal>>(operations, "DecimalAverageAsync", request, CancellationToken.None)).Should().Be(6.4m);
        (await Call<Task<decimal?>>(operations, "NullableDecimalSumAsync", request, CancellationToken.None)).Should().Be(32m);
        (await Call<Task<decimal?>>(operations, "NullableDecimalAverageAsync", request, CancellationToken.None)).Should().Be(6.4m);
        fixture.Sql.Commands.Should().HaveCount(10).And.OnlyContain(command => command.Values.Contains(3) && !command.Text.Contains("Payload", StringComparison.Ordinal));
        fixture.Sql.Commands[1].Text.Should().Contain("GROUP BY");
        fixture.Sql.Commands[2].Text.Should().Contain("SUM(");
        fixture.Sql.Commands[3].Text.Should().Contain("AVG(");
        fixture.Sql.Commands[4].Text.Should().Contain("MAX(");
        fixture.Sql.Commands[5].Text.Should().Contain("MIN(");
        context.Database.GetCommandTimeout().Should().Be(17);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [SqlServerFixtureFact]
    public async Task RequestCursorSeekAndMappingStayParameterizedAndGuardsPreventSql()
    {
        await using var fixture = await SqlQueryFixture.CreateAsync();
        await using var context = fixture.Context();
        context.Database.SetCommandTimeout(17);
        var builder = Builder();
        builder.Configure(options => options.UseCommandTimeout(3));
        var entity = builder.Entity<SqlQueryItem>();
        entity.HasQuery("Read").Where<CursorReportRequest>((item, request) => request.Scope == null || item.Name != request.Scope)
            .Select(item => item.Id).OrderBy(id => id).OrderByDescendingIf((CursorReportRequest request) => request.Descending, id => id)
            .CursorPaged<ReportCursor, CursorReportRequest>((id, cursor, request) => request.Descending ? id < cursor.Id : id > cursor.Id,
                (id, formatting) => new ReportCursor(id, formatting.Descending, formatting.Scope))
            .ValidateCursor<ReportCursor, CursorReportRequest>((cursor, request) => cursor.Descending == request.Descending && cursor.Scope == request.Scope, "Cursor does not match this request.");
        entity.HasQuery("Entity").ProjectTo(item => item.Id).OrderBy(item => item.Id)
            .CursorPaged<ReportCursor, CursorReportRequest>((item, cursor, request) => item.Id > cursor.Id + request.Offset,
                (item, formatting) => new ReportCursor(item.Id, formatting.Descending, formatting.Scope));
        var operations = Operations(context, builder);
        var request = new CursorReportRequest(true, "'; --");
        var first = await Call<Task<CursorPagedResult<int, ReportCursor>>>(operations, "ReadAsync", request, new CursorPageRequest<ReportCursor>(2), CancellationToken.None);
        first.Items.Should().Equal(6, 5);
        first.HasMore.Should().BeTrue();
        first.NextCursor.Should().Be(new ReportCursor(5, true, "'; --"));
        var next = await Call<Task<CursorPagedResult<int, ReportCursor>>>(operations, "ReadAsync", request, new CursorPageRequest<ReportCursor>(first.NextCursor!, 2), CancellationToken.None);
        next.Items.Should().Equal(3, 2);
        var entityPage = await Call<Task<CursorPagedResult<int, ReportCursor>>>(operations, "EntityAsync", request with { Offset = 1, Descending = false },
            new CursorPageRequest<ReportCursor>(new ReportCursor(1, true, "'; --"), 2), CancellationToken.None);
        entityPage.Items.Should().Equal(3, 5);
        entityPage.NextCursor.Should().Be(new ReportCursor(5, false, "'; --"));
        fixture.Sql.Commands.Should().HaveCount(3).And.OnlyContain(command => command.Text.Contains("TOP") && command.Text.Contains("ORDER BY") && !command.Text.Contains("COUNT("));
        fixture.Sql.Commands[1].Text.Should().Contain("<");
        fixture.Sql.Commands[1].Values.Should().Contain(5).And.Contain("'; --");
        Func<Task> invalid = async () => await Call<Task<CursorPagedResult<int, ReportCursor>>>(operations, "ReadAsync", request with { Descending = false },
            new CursorPageRequest<ReportCursor>(first.NextCursor!, 2), CancellationToken.None);
        await invalid.Should().ThrowAsync<ArgumentException>();
        fixture.Sql.Commands.Should().HaveCount(3);
        context.Database.GetCommandTimeout().Should().Be(17);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    private static RepositoryModelBuilder Builder()
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("SqlQuery.Contracts").RequireNavigationVisibilityReview());
        builder.Entity<SqlQueryItem>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasVisibilityPolicy("Current", item => item.Visible);
        });
        builder.Entity<SqlQueryOwner>(entity =>
        {
            entity.RepositoryEnabled = false;
            entity.HasVisibilityPolicy("Live", owner => owner.Visible);
        });
        builder.Entity<SqlQueryLine>(entity =>
        {
            entity.RepositoryEnabled = false;
            entity.HasVisibilityPolicy("LiveLines", line => line.Visible);
        });
        builder.Entity<SqlQueryNote>(entity =>
        {
            entity.RepositoryEnabled = false;
            entity.HasVisibilityPolicy("PublicNotes", note => note.Visible);
        });
        return builder;
    }

    [SqlServerFixtureFact]
    public async Task UntranslatablePredicatesFailWithoutAClientFallbackAndDisposeTheOwnedContext()
    {
        await using var fixture = await SqlQueryFixture.CreateAsync();
        await using var context = fixture.Context();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases());
        builder.Entity<SqlQueryItem>().HasQuery("Unsupported").From(sources => sources.Set<SqlQueryItem>()
            .Where(item => SqlQueryMapping.CustomMatch(item.Name)).Select(item => item.Id)).List();
        var provider = new SqlQueryLeaseProvider(fixture);
        var operations = Activator.CreateInstance(Compile(context, builder).GetType(typeof(SqlQueryItem).Namespace + ".SqlQueryItemRepositoryOperations")!, provider)!;
        Func<Task> read = async () => await Call<Task<IReadOnlyList<int>>>(operations, "UnsupportedAsync", CancellationToken.None);
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*could not be translated*");
        provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
        fixture.Sql.Commands.Should().BeEmpty();
    }

    private static RepositoryModelBuilder LoadingBuilder()
    {
        var builder = Builder();
        builder.Entity<SqlQueryItem>(entity =>
        {
            entity.ReviewNavigationVisibility<SqlQueryOwner>("Live", "EF global filter protects the optional owner reference");
            entity.ReviewNavigationVisibility<SqlQueryLine>("LiveLines", "Filtered loading explicitly requires visible lines");
            entity.ReviewNavigationVisibility<SqlQueryNote>("PublicNotes", "Filtered loading explicitly requires public notes");
            entity.HasLoadProfile<SqlQueryRequest>("Detail", load =>
            {
                load.Include(item => item.Owner);
                load.Include((item, request) => item.Lines.Where(line => line.Visible && line.Quantity >= request.MinimumQuantity).OrderBy(line => line.Rank).Take(request.MaximumLines))
                    .ThenInclude((line, request) => line.Notes.Where(note => note.Visible).OrderBy(note => note.Id).Take(request.MaximumNotes));
            });
            entity.HasProjection(item => SqlQueryMapping.Detail(item)).AutoIncludeNavigations(false);
            entity.HasQuery("Split").UseLoadProfile("Detail").Where<SqlQueryRequest>((item, request) => item.Id == request.Id)
                .AsSplitQuery().RequireExplicitLoadStrategy().ProjectTo<SqlQueryDetail>().List();
            entity.HasQuery("Single").UseLoadProfile("Detail").Where<SqlQueryRequest>((item, request) => item.Id == request.Id)
                .AsSingleQuery().RequireExplicitLoadStrategy().ProjectTo<SqlQueryDetail>().List();
            entity.HasQuery("Page").UseLoadProfile("Detail").Where<SqlQueryRequest>((item, request) => item.Id == request.Id)
                .OrderBy(item => item.Id).AsSplitQuery().RequireExplicitLoadStrategy().ProjectTo<SqlQueryDetail>().Paged();
            entity.HasQuery("Stream").Load(load =>
            {
                load.Include(item => item.Owner);
                load.Include(item => item.Lines.Where(line => line.Visible).OrderBy(line => line.Rank))
                    .ThenInclude(line => line.Notes.Where(note => note.Visible).OrderBy(note => note.Id));
            }).OrderBy(item => item.Id).AsSplitQuery().RequireExplicitLoadStrategy().ProjectTo<SqlQueryDetail>().Stream();
        });
        return builder;
    }

    private static Assembly Compile(SqlQueryContext context, RepositoryModelBuilder builder) =>
        ClosedRepositoryTests.Compile(RepositoryModelFactory.Create(context, typeof(SqlQueryItem).Assembly, true, builder.Build()), rejectWarnings: true);

    private static object Operations(SqlQueryContext context, RepositoryModelBuilder builder) =>
        Activator.CreateInstance(Compile(context, builder).GetType(typeof(SqlQueryItem).Namespace + ".SqlQueryItemRepositoryOperations")!, context)!;

    private static T Call<T>(object instance, string name, params object[] arguments) => (T)instance.GetType().GetMethod(name)!.Invoke(instance, arguments)!;
}

internal sealed class SqlQueryFixture : IAsyncDisposable
{
    private readonly string connection = Environment.GetEnvironmentVariable("REPOGEN_SQLSERVER_TEST_CONNECTION")!;
    private bool created;
    internal string Prefix { get; } = "RepoGenQuery_" + Guid.NewGuid().ToString("N") + "_";
    internal MembershipSql Sql { get; } = new();
    internal SqlQueryContext Context() => new(connection, Prefix, Sql);

    internal static async Task<SqlQueryFixture> CreateAsync()
    {
        var fixture = new SqlQueryFixture();
        await using var context = fixture.Context();
        await using var transaction = await context.Database.BeginTransactionAsync();
        // Check ownership before any CREATE; all fixture DDL and seeds roll back if initialization fails.
        foreach (var suffix in new[] { "Owners", "Items", "Lines", "Notes" })
        {
            await context.Database.ExecuteSqlRawAsync($"IF OBJECT_ID(N'dbo.{fixture.Prefix}{suffix}', N'U') IS NOT NULL THROW 51000, 'Fixture table collision', 1;");
        }

        // GO belongs to SQL tooling, not T-SQL. Only this EF-generated, fixture-owned DDL is processed.
        var ddl = string.Join(Environment.NewLine, context.Database.GenerateCreateScript().Split('\n')
            .Where(static line => !line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase)));
        await context.Database.ExecuteSqlRawAsync(ddl);
        context.Set<SqlQueryOwner>().AddRange(new SqlQueryOwner { Id = 1, Name = "Ada", Region = 7, Visible = true },
            new SqlQueryOwner { Id = 2, Name = "Hidden", Region = 7, Visible = false }, new SqlQueryOwner { Id = 3, Name = "Zoe", Region = 8, Visible = true });
        context.Items.AddRange(new SqlQueryItem { Id = 1, Name = "Alpha", OwnerId = 1, Region = 7, Visible = true },
            new SqlQueryItem { Id = 2, Name = "Beta", OwnerId = 2, Region = 7, Visible = true }, new SqlQueryItem { Id = 3, Name = "Orphan", Region = 7, Visible = true },
            new SqlQueryItem { Id = 4, Name = "HiddenRoot", OwnerId = 1, Region = 7, Visible = false }, new SqlQueryItem { Id = 5, Name = "Mismatch", OwnerId = 1, Region = 8, Visible = true },
            new SqlQueryItem { Id = 6, Name = "Delta", OwnerId = 3, Region = 8, Visible = true });
        context.Set<SqlQueryLine>().AddRange(new SqlQueryLine { Id = 11, ItemId = 1, Rank = 1, Quantity = 1, Visible = true },
            new SqlQueryLine { Id = 12, ItemId = 1, Rank = 2, Quantity = 5, Visible = true }, new SqlQueryLine { Id = 13, ItemId = 1, Rank = 3, Quantity = 9, Visible = true },
            new SqlQueryLine { Id = 14, ItemId = 1, Rank = 4, Quantity = 100, Visible = false }, new SqlQueryLine { Id = 61, ItemId = 6, Rank = 1, Quantity = 2, Visible = true });
        context.Set<SqlQueryNote>().AddRange(new SqlQueryNote { Id = 111, LineId = 11, Text = "public", Visible = true },
            new SqlQueryNote { Id = 121, LineId = 12, Text = "public", Visible = true }, new SqlQueryNote { Id = 122, LineId = 12, Text = "private", Visible = false },
            new SqlQueryNote { Id = 123, LineId = 12, Text = "later", Visible = true }, new SqlQueryNote { Id = 131, LineId = 13, Text = "public", Visible = true });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        fixture.created = true;
        fixture.Sql.Commands.Clear();
        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        if (!created)
        {
            return;
        }

        await using var context = Context();
        foreach (var suffix in new[] { "Notes", "Lines", "Items", "Owners" })
        {
            await context.Database.ExecuteSqlRawAsync($"DROP TABLE [dbo].[{Prefix}{suffix}];");
        }

        created = false;
    }
}

internal sealed class SqlQueryLeaseProvider(SqlQueryFixture fixture) : IDbContextLeaseProvider<SqlQueryContext>
{
    internal List<SqlQueryContext> Contexts { get; } = [];

    public ValueTask<DbContextLease<SqlQueryContext>> AcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = fixture.Context();
        Contexts.Add(context);
        return ValueTask.FromResult(DbContextLease.Owned(context));
    }
}

public sealed class SqlQueryItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Payload { get; set; } = "unused large field";
    public int? OwnerId { get; set; }
    public int Region { get; set; }
    public bool Visible { get; set; }
    public SqlQueryOwner? Owner { get; set; }
    public List<SqlQueryLine> Lines { get; set; } = [];
}

public sealed class SqlQueryOwner
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Region { get; set; }
    public bool Visible { get; set; }
}

public sealed class SqlQueryLine
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int Rank { get; set; }
    public int Quantity { get; set; }
    public bool Visible { get; set; }
    public List<SqlQueryNote> Notes { get; set; } = [];
}

public sealed class SqlQueryNote
{
    public int Id { get; set; }
    public int LineId { get; set; }
    public string Text { get; set; } = "";
    public bool Visible { get; set; }
}

public sealed record SqlQuerySummary(int Id, string? OwnerName);

public sealed record SqlQueryLineDetail(int Quantity, IReadOnlyList<string> Notes);

public sealed record SqlQueryDetail(int Id, string? OwnerName, IReadOnlyList<SqlQueryLineDetail> Lines);

public sealed class SqlQueryTotal
{
    public int Region { get; set; }
    public int Count { get; set; }
    public int Total { get; set; }
}

public sealed record SqlQueryRequest(int Id, int MinimumQuantity, int MaximumLines, int MaximumNotes, int MinimumCount);

public static class SqlQueryMapping
{
    public static bool CustomMatch(string name) => name.Length == 5;

    public static SqlQueryDetail Detail(SqlQueryItem item) => new(item.Id, item.Owner?.Name,
        item.Lines.OrderBy(line => line.Rank).Select(line => new SqlQueryLineDetail(line.Quantity, line.Notes.OrderBy(note => note.Id).Select(note => note.Text).ToArray())).ToArray());
}

public sealed class SqlQueryContext : DbContext
{
    internal SqlQueryContext(string connection, string prefix, MembershipSql sql) : base(new DbContextOptionsBuilder<SqlQueryContext>()
        .UseSqlServer(connection, options => options.UseCompatibilityLevel(150)).ReplaceService<IModelCacheKeyFactory, SqlQueryModelKey>().AddInterceptors(sql).Options) => Prefix = prefix;

    public string Prefix { get; }
    public int DisposeCount { get; private set; }
    public int TrackedAtDisposal { get; private set; }
    public DbSet<SqlQueryItem> Items => Set<SqlQueryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqlQueryOwner>().ToTable(Prefix + "Owners", "dbo").HasQueryFilter(owner => owner.Visible);
        var items = modelBuilder.Entity<SqlQueryItem>();
        items.ToTable(Prefix + "Items", "dbo");
        items.HasOne(item => item.Owner).WithMany().HasForeignKey(item => item.OwnerId);
        items.HasMany(item => item.Lines).WithOne().HasForeignKey(line => line.ItemId);
        var lines = modelBuilder.Entity<SqlQueryLine>();
        lines.ToTable(Prefix + "Lines", "dbo");
        lines.HasMany(line => line.Notes).WithOne().HasForeignKey(note => note.LineId);
        modelBuilder.Entity<SqlQueryNote>().ToTable(Prefix + "Notes", "dbo");
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.FindProperty("Id")!.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            foreach (var property in entity.GetProperties().Where(static property => property.ClrType == typeof(string)))
            {
                property.SetMaxLength(256);
            }
        }
    }

    public override ValueTask DisposeAsync()
    {
        DisposeCount++;
        TrackedAtDisposal = ChangeTracker.Entries().Count();
        return base.DisposeAsync();
    }
}

public sealed class SqlQueryModelKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), ((SqlQueryContext)context).Prefix, designTime);
}