using System.Data.Common;
using System.Reflection;
using System.Text.Json;
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

public sealed class MultiSourceQueryTests
{
    [Fact]
    public async Task InnerAndCompositeJoinsNeedNoEfRelationshipAndExecuteInSql()
    {
        await using var fixture = await Fixture.CreateAsync();
        var inner = await fixture.Call<Task<IReadOnlyList<MultiJoinDto>>>("InnerAsync", CancellationToken.None);
        inner.Select(static row => row.OrderId).Should().Equal(1, 2, 5, 6);
        fixture.Sql.Commands.Should().ContainSingle().Which.Should().Contain("INNER JOIN");
        fixture.Sql.Commands.Clear();
        var composite = await fixture.Call<Task<IReadOnlyList<MultiJoinDto>>>("CompositeAsync", CancellationToken.None);
        composite.Select(static row => row.OrderId).Should().Equal(1, 5, 6);
        fixture.Sql.Commands.Should().ContainSingle().Which.Should().Contain("INNER JOIN").And.Contain("\"Zone\"");
        fixture.Model.Entities.Should().ContainSingle(); // Joining a set does not expose its repository.
        fixture.Provider.Contexts.Should().OnlyContain(static context => context.DisposeCount == 1);
    }

    [Fact]
    public async Task LeftJoinsPreserveMissingRowsAndNullableColumns()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Call<Task<IReadOnlyList<MultiJoinDto>>>("LeftAsync", CancellationToken.None);
        result.Should().HaveCount(6);
        result[0].Party.Should().Be("North");
        result[1].Party.Should().BeNull(); // A mismatched composite key.
        result[2].PartyId.Should().BeNull(); // A filtered-out right row.
        result[3].Party.Should().BeNull(); // An absent right row.
        fixture.Sql.Commands.Should().ContainSingle().Which.Should().Contain("LEFT JOIN");
        fixture.Provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task CorrelatedExistsAntiJoinsAndSelectManyStayServerSide()
    {
        await using var fixture = await Fixture.CreateAsync();
        (await fixture.Call<Task<IReadOnlyList<int>>>("MatchedAsync", CancellationToken.None)).Should().Equal(1, 5, 6);
        fixture.Sql.Commands.Should().ContainSingle().Which.Should().Contain("EXISTS");
        fixture.Sql.Commands.Clear();
        (await fixture.Call<Task<IReadOnlyList<int>>>("UnmatchedAsync", CancellationToken.None)).Should().Equal(2, 3, 4);
        fixture.Sql.Commands.Should().ContainSingle().Which.Should().Contain("NOT EXISTS");
        fixture.Sql.Commands.Clear();
        (await fixture.Call<Task<IReadOnlyList<string>>>("PairsAsync", CancellationToken.None)).Should().Equal("North", "South");
        fixture.Sql.Commands.Should().ContainSingle().Which.Should().Contain("CROSS JOIN");
    }

    [Theory]
    [InlineData("Union", "UNION", new[] { 1, 2, 3, 4, 5, 6 })]
    [InlineData("Concat", "UNION ALL", new[] { 1, 1, 2, 2, 3, 4, 5, 6 })]
    [InlineData("Except", "EXCEPT", new[] { 3, 4, 5, 6 })]
    [InlineData("Intersect", "INTERSECT", new[] { 1, 2 })]
    public async Task SetOperationsAreComposedInSql(string operation, string sql, int[] expected)
    {
        await using var fixture = await Fixture.CreateAsync();
        (await fixture.Call<Task<IReadOnlyList<int>>>(operation + "Async", CancellationToken.None)).Should().Equal(expected);
        fixture.Sql.Commands.Should().ContainSingle().Which.Should().Contain(sql);
    }

    [Fact]
    public async Task RequestBindingContinuesFromJoinedRootsThroughGroupedHavingAndPaging()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new MultiRequest(0, 2);
        var page = await fixture.Call<Task<PagedResult<MultiTotal>>>("TotalsAsync", request, 1, 10, CancellationToken.None);
        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Should().Be(new MultiTotal("North", 2, 70));
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands[1].Should().Contain("INNER JOIN").And.Contain("GROUP BY").And.Contain("HAVING").And.Contain("LIMIT");
        var parameters = fixture.Model.Entities[0].Queries.Single(static query => query.MethodName == "TotalsAsync").Parameters;
        parameters.Should().ContainSingle().Which.Name.Should().Be("request");
        (await fixture.Call<Task<PagedResult<MultiTotal>>>("TotalsAsync", request with { MinimumAmount = 55, MinimumCount = 0 }, 1, 10, CancellationToken.None))
            .Items.Should().ContainSingle().Which.Total.Should().Be(60);
        fixture.Provider.Contexts.Should().OnlyContain(static context => context.DisposeCount == 1);
    }

    [Fact]
    public async Task QueryFiltersApplyToEverySourceAndBypassIsDesignerExplicit()
    {
        await using var fixture = await Fixture.CreateAsync();
        (await fixture.Call<Task<IReadOnlyList<MultiJoinDto>>>("InnerAsync", CancellationToken.None)).Should().HaveCount(4);
        fixture.Sql.Commands.Should().ContainSingle().Which.Should().Contain("\"Hidden\"");
        fixture.Sql.Commands.Clear();
        var all = await fixture.Call<Task<IReadOnlyList<MultiJoinDto>>>("IncludingHiddenAsync", CancellationToken.None);
        all.Select(static row => row.OrderId).Should().Equal(1, 2, 3, 5, 6);
        fixture.Sql.Commands.Should().ContainSingle().Which.Should().NotContain("\"Hidden\"");
        fixture.Provider.Contexts.Should().OnlyContain(static context => context.TrackedCountAtDisposal == 0);
    }

    [Fact]
    public async Task JoinedStreamsAndDictionariesUseTheSameOwnedLeaseAndHonorCancellation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dictionary = await fixture.Call<Task<IReadOnlyDictionary<int, string>>>("NamesAsync", CancellationToken.None);
        dictionary.Keys.Should().BeEquivalentTo<int>([1, 2, 5, 6]);
        fixture.Sql.Commands.Should().ContainSingle().Which.Should().Contain("INNER JOIN").And.NotContain("\"Amount\"");
        var before = fixture.Provider.Contexts.Count;
        await using (var iterator = fixture.Call<IAsyncEnumerable<MultiJoinDto>>("StreamAsync", CancellationToken.None).GetAsyncEnumerator())
        {
            fixture.Provider.Contexts.Should().HaveCount(before);
            (await iterator.MoveNextAsync()).Should().BeTrue();
            fixture.Provider.Contexts.Should().HaveCount(before + 1);
            fixture.Provider.Contexts[^1].DisposeCount.Should().Be(0);
        }

        fixture.Provider.Contexts[^1].DisposeCount.Should().Be(1);
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        Func<Task> canceled = () => fixture.Call<Task<IReadOnlyList<MultiJoinDto>>>("InnerAsync", cancel.Token);
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        fixture.Provider.Contexts.Should().HaveCount(before + 1);
    }

    [Fact]
    public async Task LegacyRepositoriesResolveOneContextAndInferCacheDependenciesThroughNavigations()
    {
        await using var fixture = await Fixture.CreateAsync();
        var model = Model(fixture.Context, new LegacyDesigner());
        var query = model.Entities.Single(static entity => entity.EntityName == nameof(MultiOrder)).Queries.Single();
        query.Cache.DependentEntityTypeNames.Should().Contain(RepositoryModelFactory.CSharpTypeName(typeof(MultiParty)))
            .And.Contain(RepositoryModelFactory.CSharpTypeName(typeof(MultiPartyGroup)));
        model.Entities.Single(static entity => entity.EntityName == nameof(MultiParty)).CacheInvalidationTags.Should().Contain(typeof(MultiOrder).FullName!);
        model.Entities.Single(static entity => entity.EntityName == nameof(MultiPartyGroup)).CacheInvalidationTags.Should().Contain(typeof(MultiOrder).FullName!);
        var assembly = ClosedRepositoryTests.Compile(model);
        var resolverType = assembly.GetType("RonSijm.RepoGen.IDbContextResolver`1")!.MakeGenericType(typeof(MultiContext));
        var resolver = DispatchProxy.Create(resolverType, typeof(MultiResolverProxy));
        var counter = (MultiResolverProxy)resolver;
        counter.Context = fixture.Context;
        var repository = Activator.CreateInstance(assembly.GetType(typeof(MultiOrder).Namespace + ".MultiOrderRepository")!, resolver)!;
        var extensions = assembly.GetType(query.Namespace + ".MultiOrderConfiguredQueryExtensions")!;
        var result = await (Task<IReadOnlyList<string>>)extensions.GetMethod("GroupNamesAsync")!.Invoke(null, [repository, CancellationToken.None])!;
        result.Should().Equal("Visible group");
        counter.Calls.Should().Be(1);
        fixture.Context.DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task SavingAnotherGeneratedRepositoryInvalidatesTheJoinedOwnersTag()
    {
        await using var fixture = await Fixture.CreateAsync();
        var model = Model(fixture.Context, new LegacyDesigner());
        var assembly = ClosedRepositoryTests.Compile(model);
        var resolverType = assembly.GetType("RonSijm.RepoGen.ScopedDbContextResolver`1")!.MakeGenericType(typeof(MultiContext));
        var resolver = Activator.CreateInstance(resolverType, fixture.Context)!;
        var repository = Activator.CreateInstance(assembly.GetType(typeof(MultiParty).Namespace + ".MultiPartyRepository")!, resolver)!;
        var cache = DispatchProxy.Create(assembly.GetType("RonSijm.RepoGen.IRepositoryCacheProvider")!, typeof(MultiCacheProxy));
        var decorator = Activator.CreateInstance(assembly.GetType(typeof(MultiParty).Namespace + ".MultiPartyRepositoryCacheDecorator")!, repository, cache)!;
        var party = await fixture.Context.Set<MultiParty>().SingleAsync(static party => party.Id == 1);
        party.Name = "Updated";
        (await (Task<int>)decorator.GetType().GetMethod("SaveChangesAsync", [typeof(CancellationToken)])!.Invoke(decorator, [CancellationToken.None])!).Should().Be(1);
        ((MultiCacheProxy)cache).Invalidated.Should().Contain(typeof(MultiParty).FullName!).And.Contain(typeof(MultiOrder).FullName!);
    }

    [Fact]
    public async Task UnsupportedServerPredicatesFailWithoutClientFilteringAndDisposeTheirContext()
    {
        await using var fixture = await Fixture.CreateAsync();
        Func<Task> unsupported = () => fixture.Call<Task<IReadOnlyList<int>>>("UnsupportedAsync", CancellationToken.None);
        await unsupported.Should().ThrowAsync<InvalidOperationException>().WithMessage("*could not be translated*");
        fixture.Sql.Commands.Should().BeEmpty();
        fixture.Provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task QueryPoliciesAndGeneratedNamesWorkOnEntityRoots()
    {
        await using var fixture = await Fixture.CreateAsync();
        (await fixture.Call<Task<IReadOnlyList<int>>>("ReservedAliasesAsync", CancellationToken.None)).Should().Equal(1, 2, 3, 4, 5, 6);
        fixture.Sql.Commands.Should().ContainSingle().Which.Should().Contain("multi-source-policy");
        fixture.Provider.Contexts[0].TrackedCountAtDisposal.Should().Be(0);
        var design = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        design.Entity<MultiOrder>().HasQuery("Tracked").AsTracking().From(sources => sources.Set<MultiOrder>()).OrderBy(order => order.Id).List();
        var assembly = ClosedRepositoryTests.Compile(RepositoryModelFactory.Create(fixture.Context, typeof(MultiContext).Assembly, design: design.Build()));
        var repository = Activator.CreateInstance(assembly.GetType(typeof(MultiOrder).Namespace + ".MultiOrderRepository")!, fixture.Context)!;
        var extension = assembly.GetType(typeof(MultiOrder).Namespace + ".MultiOrderConfiguredQueryExtensions")!;
        var result = await (Task<IReadOnlyList<MultiOrder>>)extension.GetMethod("TrackedAsync")!.Invoke(null, [repository, CancellationToken.None])!;
        result.Select(static order => order.Id).Should().Equal(1, 2, 3, 4, 5, 6);
        fixture.Context.ChangeTracker.Entries().Should().HaveCount(6);
    }

    [Fact]
    public void NavigationDependenciesContinueAfterSourceProjectionAndDefaultsDoNotAddSourceQueries()
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateCacheDecorator());
        builder.Entity<MultiOrder>().HasQuery("Related").Cache()
            .From(sources => sources.Set<MultiParty>()).Where(party => party.Group != null)
            .Select(party => party.Group!.Name).List();
        var configured = RepositoryModelFactory.Create(context, typeof(MultiContext).Assembly, design: builder.Build());
        configured.Entities.Single(static entity => entity.EntityName == nameof(MultiOrder)).Queries.Single().Cache.DependentEntityTypeNames
            .Should().Contain(RepositoryModelFactory.CSharpTypeName(typeof(MultiPartyGroup)));
        var simple = RepositoryModelFactory.Create(context, typeof(MultiContext).Assembly,
            design: new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()).Build());
        simple.Entities.SelectMany(static entity => entity.Queries).Should().BeEmpty();
        RepositorySourceRenderer.Render(simple).Should().NotContain("__repoGenContext").And.NotContain("RepositoryQuerySources");
    }

    [Fact]
    public void ContractsRemainEfFreeAndManifestsDeclareSourcesAndOperators()
    {
        using var context = Context();
        var model = Model(context, new QueryDesigner());
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(model);
        contracts.Should().NotContain("RepositoryQuerySources").And.NotContain("IQueryable").And.NotContain("EntityFrameworkCore").And.NotContain("IWriteMultiOrderRepository");
        const string dtos = "namespace RonSijm.RepoGen.Tool.Tests { public sealed record MultiJoinDto(int OrderId,string? Party,int Amount,int? PartyId); public sealed record MultiTotal(string Party,int Count,int Total); public sealed record MultiRequest(int MinimumAmount,int MinimumCount); }";
        var compilation = CSharpCompilation.Create("MultiContracts", [CSharpSyntaxTree.ParseText(contracts), CSharpSyntaxTree.ParseText("#nullable enable\n" + dtos)],
            ClosedRepositoryTests.PlatformReferences().Append(MetadataReference.CreateFromFile(typeof(PagedResult<>).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        using var manifest = JsonDocument.Parse(RepositoryManifestRenderer.Render(model));
        var left = manifest.RootElement.GetProperty("entities")[0].GetProperty("queries").EnumerateArray().Single(static item => item.GetProperty("name").GetString() == "LeftAsync");
        left.GetProperty("sources").EnumerateArray().Select(static item => item.GetString()).Should().BeEquivalentTo(
            RepositoryModelFactory.CSharpTypeName(typeof(MultiOrder)), RepositoryModelFactory.CSharpTypeName(typeof(MultiParty)));
        left.GetProperty("sourceOperators").EnumerateArray().Select(static item => item.GetString()).Should().Contain("GroupJoin").And.Contain("SelectMany").And.Contain("DefaultIfEmpty");
    }

    [Theory]
    [InlineData("unmapped", "EF model")]
    [InlineData("external", "opaque")]
    [InlineData("client", "client-query boundary")]
    [InlineData("captured", "captures configuration-time state")]
    [InlineData("unsafeRequest", "EF-free")]
    [InlineData("noRoots", "at least one")]
    [InlineData("anonymousFinal", "anonymous final result")]
    [InlineData("lateEntityClause", "Entity-level clauses")]
    public void UnsafeOrUnsupportedSourceDesignsAreRejected(string scenario, string diagnostic)
    {
        using var context = Context();
        Action create = () => Model(context, new InvalidDesigner(scenario));
        create.Should().Throw<InvalidOperationException>().WithMessage("*" + diagnostic + "*");
    }

    [Fact]
    public void SourceRootsCannotSilentlyDiscardConfiguredEntityClausesOrRestartAResultPipeline()
    {
        var entity = new RepositoryModelBuilder().Entity<MultiOrder>();
        Action misplaced = () => entity.HasQuery("Bad").Where(order => order.Amount > 0).From(sources => sources.Set<MultiOrder>().Select(order => order.Id));
        misplaced.Should().Throw<InvalidOperationException>().WithMessage("*replaces the entity root*");
        var root = entity.HasQuery("Good");
        var results = root.From(sources => sources.Set<MultiOrder>().Select(order => order.Id));
        Action restart = () => root.From(sources => sources.Set<MultiParty>().Select(party => party.Id));
        restart.Should().Throw<InvalidOperationException>().WithMessage("*only be configured once*");
        results.Select(id => id + 1);
        Action stale = () => results.Where(id => id > 0);
        stale.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
    }

    private static MultiContext Context() => new(new DbContextOptionsBuilder<MultiContext>().UseSqlite("Data Source=:memory:").Options);

    private static RepositoryGenerationModel Model(MultiContext context, RepositoryDesigner designer) =>
        RepositoryModelFactory.Create(context, typeof(MultiContext).Assembly, design: designer.CreateModel());

    private sealed class Fixture(
        SqliteConnection connection,
        MultiContext context,
        MultiSql sql,
        MultiProvider provider,
        RepositoryGenerationModel model,
        object operations) : IAsyncDisposable
    {
        public MultiContext Context { get; } = context;
        public MultiSql Sql { get; } = sql;
        public MultiProvider Provider { get; } = provider;
        public RepositoryGenerationModel Model { get; } = model;
        public TResult Call<TResult>(string method, params object[] arguments) => (TResult)operations.GetType().GetMethod(method)!.Invoke(operations, arguments)!;

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var sql = new MultiSql();
            var context = new MultiContext(new DbContextOptionsBuilder<MultiContext>().UseSqlite(connection).AddInterceptors(sql).Options);
            await context.Database.EnsureCreatedAsync();
            context.Add(new MultiPartyGroup { Id = 1, Name = "Visible group" });
            context.AddRange(new MultiParty { Id = 1, Zone = 1, Name = "North", GroupId = 1 },
                new MultiParty { Id = 2, Zone = 2, Name = "South", GroupId = 1 }, new MultiParty { Id = 3, Zone = 1, Name = "Hidden", Hidden = true });
            context.AddRange(new MultiOrder { Id = 1, PartyId = 1, Zone = 1, Amount = 10 }, new MultiOrder { Id = 2, PartyId = 1, Zone = 2, Amount = 20 },
                new MultiOrder { Id = 3, PartyId = 3, Zone = 1, Amount = 30 }, new MultiOrder { Id = 4, PartyId = 9, Zone = 1, Amount = 40 },
                new MultiOrder { Id = 5, PartyId = 2, Zone = 2, Amount = 50 }, new MultiOrder { Id = 6, PartyId = 1, Zone = 1, Amount = 60 });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            sql.Commands.Clear();
            var provider = new MultiProvider(connection, sql);
            var model = MultiSourceQueryTests.Model(context, new QueryDesigner());
            var assembly = ClosedRepositoryTests.Compile(model);
            var operations = Activator.CreateInstance(assembly.GetType(typeof(MultiOrder).Namespace + ".MultiOrderRepositoryOperations")!, provider)!;
            return new Fixture(connection, context, sql, provider, model, operations);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class MultiSql : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class MultiProvider(SqliteConnection connection, MultiSql sql) : IDbContextLeaseProvider<MultiContext>
    {
        public List<MultiContext> Contexts { get; } = [];

        public ValueTask<DbContextLease<MultiContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new MultiContext(new DbContextOptionsBuilder<MultiContext>().UseSqlite(connection).AddInterceptors(sql).Options);
            Contexts.Add(context);
            return ValueTask.FromResult(DbContextLease.Owned(context));
        }
    }

    private sealed class QueryDesigner : RepositoryDesigner
    {
        protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
        {
            modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.Multi").UseAsyncContextLeases().GenerateOnlyConfiguredEntities());
            var entity = modelBuilder.Entity<MultiOrder>();
            entity.WriteEnabled = false;
            entity.HasDefaultSort(order => order.Id).Descending(); // Not inherited by a replacement root.
            Join(entity, "Inner").List();
            Join(entity, "IncludingHidden", includeHidden: true).List();
            Join(entity, "Stream").Stream();
            entity.HasQuery("Names").From(sources => from order in sources.Set<MultiOrder>()
                                                     join party in sources.Set<MultiParty>() on order.PartyId equals party.Id
                                                     select new { order.Id, party.Name }).Dictionary(row => row.Id, row => row.Name);
            entity.HasQuery("Composite").From(sources => from order in sources.Set<MultiOrder>()
                                                         join party in sources.Set<MultiParty>() on new { Id = order.PartyId, order.Zone } equals new { party.Id, party.Zone }
                                                         orderby order.Id
                                                         select new MultiJoinDto(order.Id, party.Name, order.Amount, party.Id)).List();
            entity.HasQuery("Left").From(sources => from order in sources.Set<MultiOrder>()
                                                    join party in sources.Set<MultiParty>() on new { Id = order.PartyId, order.Zone } equals new { party.Id, party.Zone } into matches
                                                    from party in matches.DefaultIfEmpty()
                                                    orderby order.Id
                                                    select new MultiJoinDto(order.Id, party == null ? null : party.Name, order.Amount, party == null ? null : party.Id))
                .List();
            entity.HasQuery("Matched").From(sources => sources.Set<MultiOrder>()
                .Where(order => sources.Set<MultiParty>().Any(party => party.Id == order.PartyId && party.Zone == order.Zone))
                .Select(order => order.Id)).OrderBy(id => id).List();
            entity.HasQuery("Unmatched").From(sources => sources.Set<MultiOrder>()
                .Where(order => !sources.Set<MultiParty>().Any(party => party.Id == order.PartyId && party.Zone == order.Zone))
                .Select(order => order.Id)).OrderBy(id => id).List();
            entity.HasQuery("Pairs").From(sources => from order in sources.Set<MultiOrder>()
                                                     where order.Id == 1
                                                     from party in sources.Set<MultiParty>()
                                                     select party.Name).OrderBy(name => name).List();
            entity.HasQuery("Union").From(sources => sources.Set<MultiOrder>().Select(order => order.Id).Union(sources.Set<MultiParty>().Select(party => party.Id))).OrderBy(id => id).List();
            entity.HasQuery("Concat").From(sources => sources.Set<MultiOrder>().Select(order => order.Id).Concat(sources.Set<MultiParty>().Select(party => party.Id))).OrderBy(id => id).List();
            entity.HasQuery("Except").From(sources => sources.Set<MultiOrder>().Select(order => order.Id).Except(sources.Set<MultiParty>().Select(party => party.Id))).OrderBy(id => id).List();
            entity.HasQuery("Intersect").From(sources => sources.Set<MultiOrder>().Select(order => order.Id).Intersect(sources.Set<MultiParty>().Select(party => party.Id))).OrderBy(id => id).List();
            entity.HasQuery("Totals").From((RepositoryQuerySources sources, MultiRequest request) =>
                    from order in sources.Set<MultiOrder>()
                    where order.Amount >= request.MinimumAmount
                    join party in sources.Set<MultiParty>() on new { Id = order.PartyId, order.Zone } equals new { party.Id, party.Zone }
                    select new { party.Name, order.Amount })
                .GroupBy(row => row.Name, group => new { Party = group.Key, Count = group.Count(), Total = group.Sum(row => row.Amount) })
                .WhereIf<MultiRequest>(request => request.MinimumCount > 0, (total, request) => total.Count >= request.MinimumCount)
                .OrderByDescending(total => total.Total).ThenBy(total => total.Party)
                .Select(total => new MultiTotal(total.Party, total.Count, total.Total)).Paged();
            entity.HasQuery("Unsupported").From(sources => sources.Set<MultiParty>()
                .Where(party => MultiQueryHelpers.Decorate(party.Name) == "North").Select(party => party.Id)).List();
            entity.HasQuery("ReservedAliases").AsNoTracking().AsSingleQuery().TagWith("multi-source-policy")
                .From(sources => sources.Set<MultiOrder>().Where(selector => selector.Amount > 0).Select(__repoGenContext => __repoGenContext.Id))
                .OrderBy(id => id).List();
        }

        private static RepositoryResultQueryBuilder<MultiJoinDto> Join(RepositoryEntityBuilder<MultiOrder> entity, string name, bool includeHidden = false) =>
            entity.HasQuery(name).IgnoreQueryFilters(includeHidden).From(sources => from order in sources.Set<MultiOrder>()
                                                                                    join party in sources.Set<MultiParty>() on order.PartyId equals party.Id
                                                                                    orderby order.Id
                                                                                    select new MultiJoinDto(order.Id, party.Name, order.Amount, party.Id));
    }

    private sealed class LegacyDesigner : RepositoryDesigner
    {
        protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
        {
            modelBuilder.Configure(options => options.UseSimpleDefaults().UseDbContextResolver().GenerateCacheDecorator());
            modelBuilder.Entity<MultiOrder>().HasQuery("GroupNames").Cache().AsNoTracking()
                .From(sources => from order in sources.Set<MultiOrder>()
                                 join party in sources.Set<MultiParty>() on order.PartyId equals party.Id
                                 where party.Group != null
                                 select party.Group!.Name).Distinct().OrderBy(name => name).List();
        }
    }

    private sealed class InvalidDesigner(string scenario) : RepositoryDesigner
    {
        protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
        {
            modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.Multi").GenerateOnlyConfiguredEntities());
            var entity = modelBuilder.Entity<MultiOrder>();
            var query = entity.HasQuery("Invalid");
            if (scenario == "unmapped")
            {
                query.From(sources => sources.Set<MultiUnmapped>().Select(row => row.Id)).List();
            }

            if (scenario == "external")
            {
                query.From(sources => MultiQueryHelpers.Opaque(sources.Set<MultiOrder>())).List();
            }

            if (scenario == "client")
            {
                query.From(sources => sources.Set<MultiOrder>().AsEnumerable().Select(row => row.Id).AsQueryable()).List();
            }

            if (scenario == "captured")
            {
                var captured = 42;
                query.From(sources => sources.Set<MultiOrder>().Where(row => row.Id == captured).Select(row => row.Id)).List();
            }

            if (scenario == "unsafeRequest")
            {
                query.From((RepositoryQuerySources sources, MultiOrder request) => sources.Set<MultiOrder>().Where(row => row.Id == request.Id).Select(row => row.Id)).List();
            }

            if (scenario == "noRoots")
            {
                query.From(sources => (IQueryable<int>)null!).List();
            }

            if (scenario == "anonymousFinal")
            {
                query.From(sources => sources.Set<MultiOrder>().Select(row => new { row.Id })).List();
            }

            if (scenario == "lateEntityClause")
            {
                query.From(sources => sources.Set<MultiOrder>().Select(row => row.Id)).List();
                query.Where(row => row.Id > 0);
            }
        }
    }
}

public sealed record MultiJoinDto(int OrderId, string? Party, int Amount, int? PartyId);

public sealed record MultiTotal(string Party, int Count, int Total);

public sealed record MultiRequest(int MinimumAmount, int MinimumCount);

public sealed class MultiOrder
{
    public int Id { get; set; }
    public int PartyId { get; set; }
    public int Zone { get; set; }
    public int Amount { get; set; }
}

public sealed class MultiParty
{
    public int Id { get; set; }
    public int Zone { get; set; }
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
    public int? GroupId { get; set; }
    public MultiPartyGroup? Group { get; set; }
}

public sealed class MultiPartyGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class MultiUnmapped
{
    public int Id { get; set; }
}

public static class MultiQueryHelpers
{
    public static IQueryable<int> Opaque(IQueryable<MultiOrder> orders) => orders.Select(static order => order.Id);
    public static string Decorate(string name) => "[" + name + "]";
}

public class MultiResolverProxy : DispatchProxy
{
    public MultiContext Context { get; set; } = null!;
    public int Calls { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name != "Resolve")
        {
            throw new NotSupportedException();
        }

        Calls++;
        return Context;
    }
}

public class MultiCacheProxy : DispatchProxy
{
    public List<string> Invalidated { get; } = [];

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name != "InvalidateTagAsync")
        {
            throw new NotSupportedException();
        }

        Invalidated.Add((string)args![0]!);
        return Task.CompletedTask;
    }
}

public sealed class MultiContext(DbContextOptions<MultiContext> options) : DbContext(options)
{
    public int DisposeCount { get; private set; }
    public int TrackedCountAtDisposal { get; private set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MultiOrder>().ToTable("Orders");
        modelBuilder.Entity<MultiParty>().ToTable("Parties").HasQueryFilter(party => !party.Hidden);
        modelBuilder.Entity<MultiParty>().HasOne(party => party.Group).WithMany().HasForeignKey(party => party.GroupId);
        modelBuilder.Entity<MultiPartyGroup>().ToTable("PartyGroups");
    }

    public override ValueTask DisposeAsync()
    {
        TrackedCountAtDisposal = ChangeTracker.Entries().Count();
        DisposeCount++;
        return base.DisposeAsync();
    }
}