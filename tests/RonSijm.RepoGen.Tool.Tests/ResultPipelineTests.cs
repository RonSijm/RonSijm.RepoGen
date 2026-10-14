using System.Data.Common;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ResultPipelineTests
{
    [Fact]
    public async Task GroupedResultFiltersSortsAndPagesExecuteInSql()
    {
        await using var harness = await Harness.CreateAsync();
        var request = new PipelineRequest(0, 2, null, 0, 3);
        var rows = await harness.Call<Task<IReadOnlyList<PipelineReport>>>("ReportAsync", request, CancellationToken.None);
        rows.Select(static row => row.Category).Should().Equal("C", "B", "A");
        rows.Select(static row => row.Total).Should().Equal(110, 70, 30);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("GROUP BY").And.Contain("HAVING").And.Contain("ORDER BY");

        harness.Sql.Commands.Clear();
        var page = await harness.Call<Task<PagedResult<PipelineReport>>>("ReportPageAsync", request, 2, 1, CancellationToken.None);
        page.Items.Should().ContainSingle().Which.Category.Should().Be("B");
        page.TotalCount.Should().Be(3); // Groups after HAVING, not source rows.
        harness.Sql.Commands.Should().HaveCount(2);
        harness.Sql.Commands[1].Should().Contain("LIMIT").And.Contain("OFFSET");

        harness.Sql.Commands.Clear();
        var selected = await harness.Call<Task<IReadOnlyList<PipelineReport>>>("ReportAsync", request with { MinimumCount = 0, Category = "D" }, CancellationToken.None);
        selected.Should().ContainSingle().Which.Count.Should().Be(1);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().NotContain("HAVING");
        harness.Provider.Contexts.Should().OnlyContain(static context => context.DisposeCount == 1);
    }

    [Fact]
    public async Task ProjectedStagesSupportDistinctScalarAndOptionalResults()
    {
        await using var harness = await Harness.CreateAsync();
        (await harness.Call<Task<int>>("CategoryCountAsync", CancellationToken.None)).Should().Be(4);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("DISTINCT");
        (await harness.Call<Task<IReadOnlyList<string>>>("CategoryNamesAsync", "B", CancellationToken.None)).Should().Equal("B");
        (await harness.Call<Task<bool>>("AnyCategoryAsync", "X", CancellationToken.None)).Should().BeFalse();
        (await harness.Call<Task<int>>("TotalAsync", CancellationToken.None)).Should().Be(280);
        (await harness.Call<Task<double>>("AverageAsync", CancellationToken.None)).Should().Be(40);
        (await harness.Call<Task<int>>("MaximumAsync", CancellationToken.None)).Should().Be(70);
        (await harness.Call<Task<int?>>("MinimumAsync", 100, CancellationToken.None)).Should().BeNull();
        (await harness.Call<Task<int?>>("FirstAsync", 100, CancellationToken.None)).Should().BeNull();
        (await harness.Call<Task<int?>>("FirstAsync", 30, CancellationToken.None)).Should().Be(40);
        (await harness.Call<Task<PipelineReport?>>("SingleGroupAsync", "C", CancellationToken.None))!.Total.Should().Be(110);
    }

    [Fact]
    public async Task DictionarySelectsOnlyKeyAndValueAndRejectsDuplicateKeys()
    {
        await using var harness = await Harness.CreateAsync();
        var dictionary = await harness.Call<Task<IReadOnlyDictionary<int, string>>>("NamesByIdAsync", CancellationToken.None);
        dictionary.Should().HaveCount(7);
        dictionary[1].Should().Be("Alpha");
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("\"Id\"").And.Contain("\"Name\"").And.NotContain("\"Amount\"").And.NotContain("\"Category\"");
        var totals = await harness.Call<Task<IReadOnlyDictionary<string, int>>>("TotalsByCategoryAsync", CancellationToken.None);
        totals["C"].Should().Be(110);
        Func<Task> duplicate = () => harness.Call<Task<IReadOnlyDictionary<string, int>>>("DuplicateKeysAsync", CancellationToken.None);
        await duplicate.Should().ThrowAsync<ArgumentException>();
        harness.Provider.Contexts.Should().OnlyContain(static context => context.DisposeCount == 1);
    }

    [Fact]
    public async Task LookupPreservesDuplicateValuesAndSelectsOnlyTheApprovedColumns()
    {
        await using var harness = await Harness.CreateAsync();
        var lookup = await harness.Call<Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>>("NamesByCategoryAsync", CancellationToken.None);
        lookup.Should().HaveCount(4);
        lookup["A"].Should().Equal("Alpha", "Beta");
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("\"Name\"").And.Contain("\"Category\"").And.NotContain("\"Amount\"");
        (await harness.Call<Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>>("EmptyLookupAsync", CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task SliceUsesOneBoundedQueryAndChecksInputsBeforeSql()
    {
        await using var harness = await Harness.CreateAsync();
        var slice = await harness.Call<Task<SliceResult<string>>>("CategoriesSliceAsync", 1, 2, CancellationToken.None);
        slice.Items.Should().Equal("A", "B");
        slice.Page.Should().Be(1);
        slice.PageSize.Should().Be(2);
        slice.HasMore.Should().BeTrue();
        slice.NextOffset.Should().Be(2);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("DISTINCT").And.Contain("LIMIT").And.NotContain("COUNT(");
        var last = await harness.Call<Task<SliceResult<string>>>("CategoriesSliceAsync", 2, 2, CancellationToken.None);
        last.Items.Should().Equal("C", "D");
        last.HasMore.Should().BeFalse();
        last.NextOffset.Should().BeNull();
        (await harness.Call<Task<SliceResult<string>>>("CategoriesSliceAsync", 3, 2, CancellationToken.None)).Items.Should().BeEmpty();

        harness.Sql.Commands.Clear();
        Func<Task> invalidPage = () => harness.Call<Task<SliceResult<string>>>("CategoriesSliceAsync", 0, 2, CancellationToken.None);
        await invalidPage.Should().ThrowAsync<ArgumentOutOfRangeException>();
        Func<Task> invalidSize = () => harness.Call<Task<SliceResult<string>>>("CategoriesSliceAsync", 1, 0, CancellationToken.None);
        await invalidSize.Should().ThrowAsync<ArgumentOutOfRangeException>();
        Func<Task> overflow = () => harness.Call<Task<SliceResult<string>>>("CategoriesSliceAsync", int.MaxValue, 2, CancellationToken.None);
        await overflow.Should().ThrowAsync<OverflowException>();
        harness.Sql.Commands.Should().BeEmpty();
        harness.Provider.Contexts.Should().OnlyContain(static context => context.DisposeCount == 1);
    }

    [Fact]
    public async Task OneRequestBindsAcrossRootResultAndLimitStagesAndStreamsRetainLease()
    {
        await using var harness = await Harness.CreateAsync();
        var request = new PipelineRequest(20, 0, null, 1, 2);
        var result = await harness.Call<Task<IReadOnlyList<int>>>("RequestedAmountsAsync", request, CancellationToken.None);
        result.Should().Equal(40, 50);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("LIMIT").And.Contain("OFFSET");
        Func<Task> negative = () => harness.Call<Task<IReadOnlyList<int>>>("RequestedAmountsAsync", request with { Offset = -1 }, CancellationToken.None);
        await negative.Should().ThrowAsync<ArgumentOutOfRangeException>();

        var before = harness.Provider.Contexts.Count;
        var stream = harness.Call<IAsyncEnumerable<string>>("StreamCategoriesAsync", CancellationToken.None);
        harness.Provider.Contexts.Should().HaveCount(before);
        var enumerator = stream.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        harness.Provider.Contexts[^1].DisposeCount.Should().Be(0);
        await enumerator.DisposeAsync();
        harness.Provider.Contexts[^1].DisposeCount.Should().Be(1);
        var count = harness.Provider.Contexts.Count;
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await using var canceledStream = harness.Call<IAsyncEnumerable<string>>("StreamCategoriesAsync", CancellationToken.None).GetAsyncEnumerator(canceled.Token);
        Func<Task> cancel = async () => { await canceledStream.MoveNextAsync(); };
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        harness.Provider.Contexts.Should().HaveCount(count);
    }

    [Fact]
    public async Task IntermediateProjectionCanRegroupReprojectAndBindRequestExpressions()
    {
        await using var harness = await Harness.CreateAsync();
        var grouped = await harness.Call<Task<IReadOnlyList<PipelineReport>>>("ProjectedGroupsAsync", CancellationToken.None);
        grouped.Select(static report => report.Category).Should().Equal("A", "B", "C");
        grouped.Select(static report => report.Total).Should().Equal(30, 70, 110);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("HAVING");

        var request = new PipelineRequest(20, 0, null, 0, 1);
        var ranks = await harness.Call<Task<IReadOnlyList<string>>>("RequestedProjectionAsync", request, CancellationToken.None);
        ranks.Should().Equal("A", "B", "B", "C", "C", "D");
        var reports = await harness.Call<Task<IReadOnlyList<PipelineReport>>>("DtoStagesAsync", CancellationToken.None);
        reports.Select(static report => report.Total).Should().Equal(20, 30);
        var branches = await harness.Call<Task<IReadOnlyList<string>>>("RootBranchesAsync", request with { Category = "B", MinimumCount = 1 }, CancellationToken.None);
        branches.Should().Equal("Delta", "Gamma");
        harness.Sql.Commands[^1].Should().Contain("WHERE");
        harness.Sql.Commands.Clear();
        (await harness.Call<Task<IReadOnlyList<string>>>("RootBranchesAsync", request, CancellationToken.None)).Should().HaveCount(7);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().NotContain("WHERE");
    }

    [Fact]
    public async Task DefaultSortCanBeDisabledAndUntranslatableFiltersDoNotRunOnTheClient()
    {
        await using var harness = await Harness.CreateAsync();
        (await harness.Call<Task<IReadOnlyList<string>>>("DefaultSortedAsync", CancellationToken.None))[0].Should().Be("Eta");
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("ORDER BY");
        harness.Sql.Commands.Clear();
        (await harness.Call<Task<IReadOnlyList<string>>>("UnorderedAsync", CancellationToken.None)).Should().HaveCount(7);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().NotContain("ORDER BY");

        harness.Sql.Commands.Clear();
        Func<Task> unsupported = () => harness.Call<Task<IReadOnlyList<string>>>("UnsupportedAsync", CancellationToken.None);
        await unsupported.Should().ThrowAsync<InvalidOperationException>().WithMessage("*could not be translated*");
        harness.Sql.Commands.Should().BeEmpty();
        harness.Provider.Contexts[^1].DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task AnonymousIntermediateRowsAndCompositeGroupKeysStayServerSide()
    {
        await using var harness = await Harness.CreateAsync();
        var report = await harness.Call<Task<IReadOnlyList<PipelineReport>>>("AnonymousGroupsAsync", CancellationToken.None);
        report.Select(static row => row.Category).Should().Equal("A", "B", "C");
        report.Select(static row => row.Total).Should().Equal(30, 70, 110);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("GROUP BY").And.Contain("HAVING");
        (await harness.Call<Task<IReadOnlyList<PipelineReport>>>("AnonymousOrderAsync", CancellationToken.None)).Select(static row => row.Total).Should().Equal(10, 20, 30);
        (await harness.Call<Task<int>>("GroupedCountAsync", CancellationToken.None)).Should().Be(4);
        (await harness.Call<Task<int>>("EmptySumAsync", CancellationToken.None)).Should().Be(0);
        Func<Task> emptyMax = () => harness.Call<Task<int>>("EmptyMaxAsync", CancellationToken.None);
        await emptyMax.Should().ThrowAsync<InvalidOperationException>();
        harness.Provider.Contexts[^1].DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task LegacySelectorsWithProjectionRestrictionsAndCacheBranchesCompileAndExecute()
    {
        await using var harness = await Harness.CreateAsync();
        var model = Model(harness.Context, new LegacyPipelineDesigner());
        var assembly = ClosedRepositoryTests.Compile(model);
        var repository = Activator.CreateInstance(assembly.GetType(typeof(PipelineRow).Namespace + ".PipelineRowRepository")!, harness.Context)!;
        var extension = assembly.GetType(typeof(PipelineReport).Namespace + ".PipelineRowConfiguredQueryExtensions")!.GetMethod("LegacyReportAsync")!;
        var result = await (Task<IReadOnlyList<PipelineReport>>)extension.Invoke(null, [repository, CancellationToken.None])!;
        result.Select(static report => report.Total).Should().Equal(20, 30, 40, 50, 60, 70);
        model.Entities[0].ProjectionEnabled.Should().BeFalse();
        model.Entities[0].Queries[0].Cache.Enabled.Should().BeTrue();
        harness.Context.DisposeCount.Should().Be(0);
    }

    [Fact]
    public void PipelineContractsCompileWithoutEfAndManifestListsNewTerminals()
    {
        using var context = new PipelineContext(new DbContextOptionsBuilder<PipelineContext>().UseSqlite("Data Source=:memory:").Options);
        var model = Model(context, new PipelineDesigner());
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(model);
        contracts.Should().NotContain("EntityFrameworkCore").And.NotContain("global::RonSijm.RepoGen.Tool.Tests.PipelineRow>");
        const string dtos = "namespace RonSijm.RepoGen.Tool.Tests { public sealed class PipelineReport { public string Category {get;set;} = \"\"; public int Count {get;set;} public int Total {get;set;} } public sealed record PipelineRequest(int MinimumAmount,int MinimumCount,string? Category,int Offset,int Limit); }";
        var compilation = CSharpCompilation.Create("PipelineContracts", [CSharpSyntaxTree.ParseText(contracts), CSharpSyntaxTree.ParseText(dtos)],
            ClosedRepositoryTests.PlatformReferences().Append(MetadataReference.CreateFromFile(typeof(SliceResult<>).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var manifest = RepositoryManifestRenderer.Render(model);
        manifest.Should().Contain("SliceResult").And.Contain("IReadOnlyDictionary");
    }

    [Theory]
    [InlineData("wrongResult", "does not match")]
    [InlineData("thenBy", "ThenBy requires")]
    [InlineData("distinctThenBy", "ThenBy requires")]
    [InlineData("whereThenBy", "ThenBy requires")]
    [InlineData("unsafeRequest", "EF-free")]
    [InlineData("captured", "captures")]
    [InlineData("cursor", "entity cursor")]
    [InlineData("reserved", "reserved generated-local prefix")]
    [InlineData("terminalShape", "terminal selector does not match")]
    [InlineData("terminalOverride", "EF-free")]
    [InlineData("anonymousFinal", "anonymous final result")]
    public void InvalidPipelineDesignsAreRejected(string scenario, string diagnostic)
    {
        using var context = new PipelineContext(new DbContextOptionsBuilder<PipelineContext>().UseSqlite("Data Source=:memory:").Options);
        Action generate = () => Model(context, new InvalidPipelineDesigner(scenario));
        generate.Should().Throw<InvalidOperationException>().WithMessage("*" + diagnostic + "*");
    }

    [Fact]
    public void StaleBuildersCannotModifyAnEarlierResultShape()
    {
        var root = new RepositoryModelBuilder().Entity<PipelineRow>().HasQuery("Stale").Select(row => row.Name);
        root.Select(name => name.Length);
        Action stale = () => root.Where(name => name.Contains("Alpha"));
        stale.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        var sameShape = new RepositoryModelBuilder().Entity<PipelineRow>().HasQuery("SameShape").Select(row => row.Name);
        sameShape.Select(name => name);
        Action staleSameShape = () => sameShape.OrderBy(name => name);
        staleSameShape.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        Action negative = () => new RepositoryModelBuilder().Entity<PipelineRow>().HasQuery("Invalid").Results<PipelineRow>().Take(-1);
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static RepositoryGenerationModel Model(PipelineContext context, RepositoryDesigner designer) =>
        RepositoryModelFactory.Create(context, typeof(PipelineContext).Assembly, design: designer.CreateModel());

    private sealed class Harness(SqliteConnection connection, PipelineContext context, PipelineSql sql, PipelineProvider provider, object operations) : IAsyncDisposable
    {
        public PipelineContext Context { get; } = context;
        public PipelineSql Sql { get; } = sql;
        public PipelineProvider Provider { get; } = provider;
        public TResult Call<TResult>(string method, params object[] arguments) => (TResult)operations.GetType().GetMethod(method)!.Invoke(operations, arguments)!;

        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var sql = new PipelineSql();
            var context = new PipelineContext(new DbContextOptionsBuilder<PipelineContext>().UseSqlite(connection).AddInterceptors(sql).Options);
            await context.Database.EnsureCreatedAsync();
            context.AddRange(
                new PipelineRow { Id = 1, Category = "A", Amount = 10, Name = "Alpha" },
                new PipelineRow { Id = 2, Category = "A", Amount = 20, Name = "Beta" },
                new PipelineRow { Id = 3, Category = "B", Amount = 30, Name = "Gamma" },
                new PipelineRow { Id = 4, Category = "B", Amount = 40, Name = "Delta" },
                new PipelineRow { Id = 5, Category = "C", Amount = 50, Name = "Echo" },
                new PipelineRow { Id = 6, Category = "C", Amount = 60, Name = "Foxtrot" },
                new PipelineRow { Id = 7, Category = "D", Amount = 70, Name = "Eta" });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            sql.Commands.Clear();
            var provider = new PipelineProvider(connection, sql);
            var assembly = ClosedRepositoryTests.Compile(Model(context, new PipelineDesigner()));
            var operations = Activator.CreateInstance(assembly.GetType(typeof(PipelineRow).Namespace + ".PipelineRowRepositoryOperations")!, provider)!;
            return new Harness(connection, context, sql, provider, operations);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class PipelineProvider(SqliteConnection connection, PipelineSql sql) : IDbContextLeaseProvider<PipelineContext>
    {
        public List<PipelineContext> Contexts { get; } = [];

        public ValueTask<DbContextLease<PipelineContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new PipelineContext(new DbContextOptionsBuilder<PipelineContext>().UseSqlite(connection).AddInterceptors(sql).Options);
            Contexts.Add(context);
            return ValueTask.FromResult(DbContextLease.Owned(context));
        }
    }

    private sealed class PipelineSql : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PipelineDesigner : RepositoryDesigner
    {
        protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
        {
            modelBuilder.Configure(options => options.UseClosedRepositories("Generated.Pipeline").UseAsyncContextLeases().GenerateOnlyConfiguredEntities().GenerateCacheDecorator(false));
            var entity = modelBuilder.Entity<PipelineRow>();
            entity.HasDefaultSort(row => row.Id).Descending();
            entity.HasProjection(row => row.Category);
            Report(entity, "Report").List();
            Report(entity, "ReportPage").Paged();
            entity.HasQuery("CategoryCount").ProjectTo<string>().Results<string>().Distinct().Count();
            entity.HasQuery("CategoryNames").ProjectTo<string>().Results<string>().Distinct().Where<string>((name, term) => name.Contains(term)).OrderBy(name => name).List();
            entity.HasQuery("AnyCategory").ProjectTo<string>().Results<string>().Where<string>((name, term) => name == term).Exists();
            entity.HasQuery("Total").Select(row => row.Amount).Sum(amount => amount);
            entity.HasQuery("Average").Select(row => row.Amount).Average(amount => amount);
            entity.HasQuery("Maximum").Select(row => row.Amount).Max(amount => amount);
            entity.HasQuery("Minimum").Where<int>((row, minimum) => row.Amount > minimum).Select(row => (int?)row.Amount).Min(amount => amount);
            entity.HasQuery("First").Select(row => row.Amount).Where<int>((amount, minimum) => amount > minimum).OrderBy(amount => amount).FirstOrDefault();
            Groups(entity, "SingleGroup").Results<PipelineReport>().Where<string>((report, category) => report.Category == category).SingleOrDefault();
            entity.HasQuery("NamesById").Results<PipelineRow>().Dictionary(row => row.Id, row => row.Name);
            Groups(entity, "TotalsByCategory").Results<PipelineReport>().Dictionary(report => report.Category, report => report.Total);
            entity.HasQuery("DuplicateKeys").Results<PipelineRow>().Dictionary(row => row.Category, row => row.Amount);
            entity.HasQuery("NamesByCategory").OrderBy(row => row.Id).Results<PipelineRow>().Lookup(row => row.Category, row => row.Name);
            entity.HasQuery("EmptyLookup").Where(row => row.Amount < 0).Results<PipelineRow>().Lookup(row => row.Category, row => row.Name);
            entity.HasQuery("CategoriesSlice").ProjectTo<string>().Results<string>().Distinct().OrderBy(name => name).Slice();
            entity.HasQuery("RequestedAmounts").Where<PipelineRequest>((row, request) => row.Amount > request.MinimumAmount)
                .Select(row => row.Amount).Where<PipelineRequest>((amount, request) => request.Category == null || amount < 100)
                .OrderBy(amount => amount).Skip<PipelineRequest>(request => request.Offset).Take<PipelineRequest>(request => request.Limit).List();
            entity.HasQuery("StreamCategories").ProjectTo<string>().Results<string>().Distinct().OrderBy(name => name).Stream();
            entity.HasQuery("ProjectedGroups").Select(row => new PipelineReport { Category = row.Category, Count = 1, Total = row.Amount })
                .GroupBy(report => report.Category, group => new PipelineReport { Category = group.Key, Count = group.Count(), Total = group.Sum(report => report.Total) })
                .Where(report => report.Count > 1).OrderBy(report => report.Category).List();
            entity.HasQuery("RequestedProjection").UseDefaultSort(false)
                .Select<PipelineReport, PipelineRequest>((row, request) => new PipelineReport { Category = row.Category, Count = 1, Total = row.Amount + request.MinimumAmount })
                .Where<PipelineRequest>((report, request) => report.Total >= request.MinimumAmount * 2)
                .OrderBy<int, PipelineRequest>((report, request) => request.MinimumCount > 0 ? -report.Total : report.Total)
                .ThenBy(report => report.Category).Select(report => report.Category).List();
            entity.HasQuery("DtoStages").Select(row => new PipelineReport { Category = row.Category, Count = 1, Total = row.Amount })
                .Where(report => report.Total > 10).OrderBy(report => report.Total).Take(2)
                .Select(report => new PipelineReport { Category = report.Category, Count = report.Count, Total = report.Total }).List();
            entity.HasQuery("DefaultSorted").ProjectTo(row => row.Name).List();
            entity.HasQuery("Unordered").UseDefaultSort(false).ProjectTo(row => row.Name).List();
            entity.HasQuery("Unsupported").Select(row => row.Name).Where(name => PipelineTransforms.Decorate(name) == "[Alpha]").List();
            entity.HasQuery("RootBranches").UseDefaultSort(false)
                .WhereIf<PipelineRequest>(request => request.Category != null, (row, request) => row.Category == request.Category)
                .OrderBy<int, PipelineRequest>((row, request) => request.MinimumCount > 0 ? -row.Amount : row.Amount)
                .ThenBy(row => row.Id).ProjectTo(row => row.Name).List();
            entity.HasQuery("AnonymousGroups").UseDefaultSort(false).Select(row => new { row.Category, row.Amount })
                .GroupBy(row => new { row.Category, Positive = row.Amount > 0 },
                    group => new PipelineReport { Category = group.Key.Category, Count = group.Count(), Total = group.Sum(entity => entity.Amount) })
                .Where(report => report.Count > 1).OrderBy(report => report.Category).List();
            entity.HasQuery("AnonymousOrder").UseDefaultSort(false).Select(row => new { row.Category, row.Amount })
                .Where(row => row.Amount > 0).OrderBy(row => row.Amount).ThenBy(row => row.Category).Take(3)
                .Select(row => new PipelineReport { Category = row.Category, Count = 1, Total = row.Amount }).List();
            Groups(entity, "GroupedCount").Count(); // Entity-level terminal now preserves the configured group.
            entity.HasQuery("EmptySum").Select(row => row.Amount).Where(amount => amount < 0).Sum(amount => amount);
            entity.HasQuery("EmptyMax").Select(row => row.Amount).Where(amount => amount < 0).Max(amount => amount);
        }

        private static RepositoryQueryBuilder<PipelineRow> Groups(RepositoryEntityBuilder<PipelineRow> entity, string name) =>
            entity.HasQuery(name).GroupBy(row => row.Category, group => new PipelineReport { Category = group.Key, Count = group.Count(), Total = group.Sum(row => row.Amount) });

        private static RepositoryResultQueryBuilder<PipelineReport> Report(RepositoryEntityBuilder<PipelineRow> entity, string name) =>
            entity.HasQuery(name).Where<PipelineRequest>((row, request) => row.Amount >= request.MinimumAmount && (request.Category == null || row.Category == request.Category))
                .GroupBy(row => row.Category, group => new PipelineReport { Category = group.Key, Count = group.Count(), Total = group.Sum(row => row.Amount) })
                .Results<PipelineReport>().WhereIf<PipelineRequest>(request => request.MinimumCount > 0, (report, request) => report.Count >= request.MinimumCount)
                .OrderByDescending(report => report.Total).ThenBy(report => report.Category);
    }

    private sealed class InvalidPipelineDesigner(string scenario) : RepositoryDesigner
    {
        protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
        {
            modelBuilder.Configure(options => options.GenerateOnlyConfiguredEntities());
            if (scenario != "anonymousFinal")
            {
                modelBuilder.Configure(options => options.UseClosedRepositories("Generated.Pipeline"));
            }

            var query = modelBuilder.Entity<PipelineRow>().HasQuery("Invalid").ProjectTo(row => row.Name);
            if (scenario == "wrongResult")
            {
                query.Results<int>().List();
            }

            if (scenario == "thenBy")
            {
                query.Results<string>().ThenBy(name => name).List();
            }

            if (scenario == "distinctThenBy")
            {
                query.Results<string>().OrderBy(name => name).Distinct().ThenBy(name => name).List();
            }

            if (scenario == "whereThenBy")
            {
                query.Results<string>().OrderBy(name => name).Where(name => name.Length > 0).ThenBy(name => name).List();
            }

            if (scenario == "unsafeRequest")
            {
                query.Results<string>().Where<PipelineRow>((name, request) => name == request.Name).List();
            }

            if (scenario == "captured")
            {
                var offset = new PipelineRequest(0, 0, null, 1, 2);
                query.Results<string>().Where(name => name.Length > offset.Offset).List();
            }

            if (scenario == "cursor")
            {
                query.Results<string>();
                query.CursorPaged<int>((row, cursor) => row.Id > cursor, row => row.Id);
            }

            if (scenario == "reserved")
            {
                query.Results<string>().Where<int>((name, __repoGenQuery0) => name.Length == __repoGenQuery0).List();
            }

            if (scenario == "terminalShape")
            {
                query.Results<string>().Max(name => name.Length).Select(name => name.Length);
            }

            if (scenario == "terminalOverride")
            {
                var original = modelBuilder.Entity<PipelineRow>().HasQuery("Override");
                original.Results<PipelineRow>().Max(row => row.Amount);
                original.List();
            }

            if (scenario == "anonymousFinal")
            {
                query.Results<string>().Select(name => new { Name = name }).List();
            }
        }
    }

    private sealed class LegacyPipelineDesigner : RepositoryDesigner
    {
        protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
        {
            modelBuilder.Configure(options => options.GenerateOnlyConfiguredEntities());
            var entity = modelBuilder.Entity<PipelineRow>();
            entity.ProjectionEnabled = false;
            entity.HasQuery("LegacyReport").Cache().UseDefaultSort(false)
                .Select(row => new PipelineReport { Category = row.Category, Count = 1, Total = row.Amount })
                .Where(report => report.Total > 10).OrderBy(report => report.Total).ThenBy(report => report.Category).List();
        }
    }
}

public sealed class PipelineRow
{
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public int Amount { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class PipelineReport
{
    public string Category { get; set; } = string.Empty;
    public int Count { get; set; }
    public int Total { get; set; }
}

public sealed record PipelineRequest(int MinimumAmount, int MinimumCount, string? Category, int Offset, int Limit);

public static class PipelineTransforms
{
    public static string Decorate(string name) => "[" + name + "]";
}

public sealed class PipelineContext(DbContextOptions<PipelineContext> options) : DbContext(options)
{
    public DbSet<PipelineRow> Rows => Set<PipelineRow>();
    public int DisposeCount { get; private set; }

    public override ValueTask DisposeAsync()
    {
        DisposeCount++;
        return base.DisposeAsync();
    }
}