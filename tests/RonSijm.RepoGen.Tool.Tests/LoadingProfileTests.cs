using System.Data.Common;
using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

#pragma warning disable CA1310 // SQL predicates deliberately use the provider's translatable collation semantics.

public sealed class LoadingProfileTests
{
    [Fact]
    public async Task TypedReferenceCollectionAndNestedCollectionProfilesExecuteWithOwnedLeases()
    {
        await using var fixture = await Fixture.CreateAsync();
        var model = Model(fixture.Context, builder => ConfigureQueries(builder));
        var assembly = ClosedRepositoryTests.Compile(model);
        var operations = fixture.Operations(assembly);
        var request = new LoadingRequest(1, 4, 10, "p", false);
        var rows = await Invoke<Task<IReadOnlyList<LoadingOrderDto>>>(operations, "DetailsAsync", request, CancellationToken.None);
        rows.Should().ContainSingle();
        rows[0].Customer.Should().Be("Ada");
        rows[0].City.Should().Be("Amsterdam");
        rows[0].Info.Should().Be("owned metadata");
        rows[0].Lines.Select(line => line.Quantity).Should().Equal(5, 9);
        rows[0].Lines.Select(line => line.Product).Should().Equal("Tea", "Coffee");
        rows[0].Lines.SelectMany(line => line.Tags).Should().OnlyContain(tag => tag.StartsWith('p'));
        rows[0].Notes.Should().Equal("public");
        fixture.Sql.Commands.Should().HaveCount(4); // Root + lines + line tags + notes.
        fixture.Sql.Commands.Should().Contain(command => command.Contains("@__request", StringComparison.Ordinal));
        fixture.Provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
        fixture.Provider.Contexts[0].TrackedAtDisposal.Should().Be(0);

        fixture.Sql.Commands.Clear();
        var optional = await Invoke<Task<IReadOnlyList<LoadingOrderDto>>>(operations, "DetailsAsync", request with { Id = 2 }, CancellationToken.None);
        optional.Should().ContainSingle().Which.Customer.Should().BeNull();
        optional[0].City.Should().BeNull();
        fixture.Provider.Contexts.Should().OnlyContain(context => context.DisposeCount == 1);
        var query = model.Entities.Single().Queries.Single(query => query.MethodName == "DetailsAsync");
        query.Parameters.Should().ContainSingle().Which.Name.Should().Be("request");
        query.LoadProfileNames.Should().Equal("Detail", "Filtered");
        query.Cache.DependentEntityTypeNames.Should().Contain(RepositoryModelFactory.CSharpTypeName(typeof(LoadingLineTag)));
        ClosedRepositorySourceRenderer.RenderContracts(model).Should().NotContain("Include").And.NotContain("LoadProfile").And.NotContain("Microsoft.EntityFrameworkCore");
        RepositoryManifestRenderer.Render(model).Should().Contain("Lines.Tags").And.Contain("Customer.Address").And.Contain("\"filtered\": true");
    }

    [Fact]
    public async Task SingleQueryAndSplitQueryPoliciesPreserveTheSameGraphAndPaging()
    {
        await using var fixture = await Fixture.CreateAsync();
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, ConfigureQueries)));
        var request = new LoadingRequest(1, 4, 1, "", true);
        var single = await Invoke<Task<IReadOnlyList<LoadingOrderDto>>>(operations, "SingleQueryDetailsAsync", request, CancellationToken.None);
        single[0].Lines.Select(line => line.Quantity).Should().Equal(5, 9);
        single[0].Lines.Should().OnlyContain(line => line.Tags.Count == 1);
        single[0].Notes.Should().HaveCount(2);
        fixture.Sql.Commands.Should().ContainSingle().Which.Should().Contain("JOIN").And.Contain("ROW_NUMBER");
        fixture.Sql.Commands.Clear();
        var page = await Invoke<Task<PagedResult<LoadingOrderDto>>>(operations, "PageDetailsAsync", request, 1, 10, CancellationToken.None);
        page.TotalCount.Should().Be(1);
        page.Items[0].Should().BeEquivalentTo(single[0]);
        fixture.Sql.Commands.Should().HaveCount(5); // Count plus four graph queries.
        fixture.Provider.Contexts.Should().OnlyContain(context => context.DisposeCount == 1);
    }

    [Fact]
    public async Task StreamingRetainsTheGraphLeaseUntilEarlyDisposalAndCleansUpOnCancellation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, ConfigureQueries)));
        var stream = Invoke<IAsyncEnumerable<LoadingOrderDto>>(operations, "StreamDetailsAsync", CancellationToken.None);
        fixture.Provider.Contexts.Should().BeEmpty();
        var enumerator = stream.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Lines.Should().HaveCount(3);
        var dto = enumerator.Current;
        fixture.Provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(0);
        await enumerator.DisposeAsync();
        fixture.Provider.Contexts[0].DisposeCount.Should().Be(1);
        dto.Customer.Should().Be("Ada"); // No lazy-loading dependency remains.

        using var cancellation = new CancellationTokenSource();
        var canceled = Invoke<IAsyncEnumerable<LoadingOrderDto>>(operations, "StreamDetailsAsync", cancellation.Token).GetAsyncEnumerator();
        (await canceled.MoveNextAsync()).Should().BeTrue();
        await cancellation.CancelAsync();
        Func<Task> move = async () => { await canceled.MoveNextAsync(); };
        await move.Should().ThrowAsync<OperationCanceledException>();
        await canceled.DisposeAsync();
        fixture.Provider.Contexts.Should().OnlyContain(context => context.DisposeCount == 1);
    }

    [Fact]
    public async Task RequestInputsCanComeOnlyFromLoadingAndBindToAnExistingApplicationPort()
    {
        await using var fixture = await Fixture.CreateAsync();
        var model = Model(fixture.Context, builder =>
        {
            var entity = builder.Entity<LoadingOrder>();
            entity.HasLoadProfile<LoadingRequest>("Lines", load =>
            {
                IRepositoryIncludeChain<LoadingOrder, LoadingRequest, LoadingProduct> chain = load.Include((order, request) =>
                        order.Lines.Where(line => line.Quantity >= request.MinimumQuantity).OrderBy(line => line.Position))
                    .ThenInclude(line => line.Product);
                chain.Configuration.ThenIncludes.Should().ContainSingle();
            });
            entity.BindReadPort<ILoadingReadPort>();
            entity.HasQuery("LoadByRequest").UseLoadProfile("Lines").AsSplitQuery()
                .ProjectTo(order => LoadingMapper.LinesOnly(order)).List();
        });
        model.Entities.Single().Queries.Single().Parameters.Should().ContainSingle();
        var operations = (ILoadingReadPort)fixture.Operations(ClosedRepositoryTests.Compile(model));
        var rows = await operations.LoadByRequestAsync(new LoadingRequest(0, 8, 10, "", false));
        rows.Should().HaveCount(2);
        rows[0].Lines.Should().ContainSingle().Which.Quantity.Should().Be(9);
        rows[1].Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectionProfilesAndEntityDefaultsWorkInLegacySelectorMode()
    {
        await using var fixture = await Fixture.CreateAsync();
        var model = Model(fixture.Context, builder =>
        {
            var entity = builder.Entity<LoadingOrder>();
            var profile = entity.HasLoadProfile("Detail", load =>
            {
                load.Include(order => order.Customer).ThenInclude(customer => customer!.Address);
                load.Include(order => order.Lines).ThenInclude(line => line.Product);
                load.Include(order => order.Lines).ThenInclude(line => line.Tags);
                load.Include(order => order.Notes);
            });
            entity.UseLoadProfile(profile);
            entity.HasProjection(order => LoadingMapper.Details(order)).UseLoadProfile("Detail").AutoIncludeNavigations(false);
            entity.ProjectionEnabled = false;
        }, closed: false);
        var assembly = ClosedRepositoryTests.Compile(model);
        var repository = Activator.CreateInstance(assembly.GetType(typeof(LoadingOrder).Namespace + ".LoadingOrderRepository")!, fixture.Context)!;
        var selector = Invoke<object>(repository, "ById", 1);
        var projections = assembly.GetType(typeof(LoadingOrderDto).Namespace + ".LoadingOrderConfiguredProjectionExtensions")!;
        var methodName = model.Entities.Single().Projections.Single().SingleMethodName;
        var dto = await (Task<LoadingOrderDto?>)projections.GetMethod(methodName, [selector.GetType(), typeof(CancellationToken)])!
            .Invoke(null, [selector, CancellationToken.None])!;
        dto!.Lines.Should().HaveCount(3);
        dto.Customer.Should().Be("Ada");
        dto.City.Should().Be("Amsterdam");
        dto.Lines[0].Product.Should().Be("Water");
        fixture.Context.ChangeTracker.Entries().Should().NotBeEmpty(); // Legacy tracking remains unchanged.
    }

    [Fact]
    public async Task UnsupportedProviderTranslationIsNotRewrittenOrEvaluatedOnTheClientAndDisposesTheLease()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new LoadingRequest(1, 4, 1, "p", false);
        Func<Task> raw = async () => await fixture.Context.Orders.AsNoTracking()
            .Include(order => order.Customer).ThenInclude(customer => customer!.Address)
            .Include(order => order.Lines).ThenInclude(line => line.Product)
            .Include(order => order.Lines.Where(line => line.Quantity >= request.MinimumQuantity)
                .OrderBy(line => line.Position).Take(request.MaximumLines))
            .ThenInclude(line => line.Tags.Where(tag => tag.Tag.StartsWith(request.TagPrefix)))
            .Include(order => order.Notes.Where(note => note.IsPublic || request.IncludePrivateNotes))
            .Where(order => order.Id == request.Id).AsSplitQuery().Select(order => LoadingMapper.Details(order)).ToListAsync();
        await raw.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SQL APPLY*");

        var model = Model(fixture.Context, builder => builder.Entity<LoadingOrder>().HasQuery("LimitedGraph")
            .Load<LoadingRequest>(load => load.Include((order, request) => order.Lines
                    .Where(line => line.Quantity >= request.MinimumQuantity).OrderBy(line => line.Position).Take(request.MaximumLines))
                .ThenInclude((line, request) => line.Product))
            .AsSplitQuery().ProjectTo(order => LoadingMapper.LinesOnly(order)).List());
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(model));
        Func<Task> generated = async () => await Invoke<Task<IReadOnlyList<LoadingOrderDto>>>(operations, "LimitedGraphAsync", request, CancellationToken.None);
        await generated.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SQL APPLY*");
        fixture.Provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
        fixture.Sql.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task PureDtoProjectionsPruneGraphIncludesAndAutomaticNavigationLoadingCanBeDisabled()
    {
        await using var fixture = await Fixture.CreateAsync();
        var model = Model(fixture.Context, builder =>
        {
            var entity = builder.Entity<LoadingOrder>();
            entity.HasLoadProfile("Graph", load => load.Include(order => order.Lines).ThenInclude(line => line.Tags));
            entity.HasProjection<string>("ProjectToName", order => order.Name).UseLoadProfile("Graph").AutoIncludeNavigations(false);
            entity.HasQuery("Names").OrderBy(order => order.Id).ProjectTo("ProjectToName").List();
            entity.HasProjection<string>("ProjectToCity", order => order.Customer == null || order.Customer.Address == null ? "" : order.Customer.Address.City)
                .AutoIncludeNavigations(false);
            entity.HasQuery("Cities").OrderBy(order => order.Id).ProjectTo("ProjectToCity").List();
        });
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(model));
        (await Invoke<Task<IReadOnlyList<string>>>(operations, "NamesAsync", CancellationToken.None)).Should().Equal("First", "Second");
        fixture.Sql.Commands.Should().ContainSingle().Which.Should().NotContain("JOIN").And.NotContain("Payload").And.NotContain("Quantity");
        fixture.Sql.Commands.Clear();
        (await Invoke<Task<IReadOnlyList<string>>>(operations, "CitiesAsync", CancellationToken.None)).Should().Equal("Amsterdam", "");
        fixture.Sql.Commands.Should().ContainSingle().Which.Should().Contain("JOIN").And.NotContain("Payload");
        var cities = model.Entities.Single().Queries.Single(query => query.MethodName == "CitiesAsync");
        cities.Includes.Should().BeEmpty();
        cities.Cache.DependentEntityTypeNames.Should().Contain(RepositoryModelFactory.CSharpTypeName(typeof(LoadingAddress)));
    }

    [Fact]
    public async Task EquivalentFiltersWithDifferentLambdaNamesCanLoadSeparateBranches()
    {
        await using var fixture = await Fixture.CreateAsync();
        var model = Model(fixture.Context, builder => builder.Entity<LoadingOrder>().HasQuery("Branches").Load(load =>
        {
            load.Include(order => order.Lines.Where(line => line.Quantity > 1)).ThenInclude(line => line.Product);
            load.Include(row => row.Lines.Where(item => item.Quantity > 1)).ThenInclude(item => item.Tags);
        }).Where<int>((order, id) => order.Id == id).AsSplitQuery().ProjectTo(order => LoadingMapper.Details(order)).List());
        var rows = await Invoke<Task<IReadOnlyList<LoadingOrderDto>>>(fixture.Operations(ClosedRepositoryTests.Compile(model)),
            "BranchesAsync", 1, CancellationToken.None);
        rows[0].Lines.Select(line => line.Product).Should().Equal("Tea", "Coffee");
        rows[0].Lines[0].Tags.Should().Equal("internal", "public");
    }

    [Fact]
    public async Task AnUnparameterizedRootInARequestProfileRetainsTheInputTypeForLaterFilteredContinuations()
    {
        await using var fixture = await Fixture.CreateAsync();
        var model = Model(fixture.Context, builder => builder.Entity<LoadingOrder>().HasQuery("Tags")
            .Load<LoadingRequest>(load =>
            {
                load.Include(order => order.Lines).ThenInclude((line, request) => line.Tags.Where(tag => tag.Tag.StartsWith(request.TagPrefix)));
                load.Include(order => order.Lines).ThenInclude(line => line.Product);
            })
            .Where<LoadingRequest>((order, request) => order.Id == request.Id).AsSplitQuery()
            .ProjectTo(order => LoadingMapper.Details(order)).List());
        var rows = await Invoke<Task<IReadOnlyList<LoadingOrderDto>>>(fixture.Operations(ClosedRepositoryTests.Compile(model)),
            "TagsAsync", new LoadingRequest(1, 0, 10, "i", false), CancellationToken.None);
        rows[0].Lines[0].Tags.Should().BeEmpty();
        rows[0].Lines[1].Tags.Should().Equal("internal");
        model.Entities.Single().Queries.Single().Parameters.Should().ContainSingle();
    }

    [Fact]
    public async Task ManyToManySkipNavigationsSupportTypedContinuationsAndDependencyTags()
    {
        await using var fixture = await Fixture.CreateAsync();
        var model = Model(fixture.Context, builder => builder.Entity<LoadingOrder>().HasQuery("Labels")
            .Load(load => load.Include(order => order.Labels).ThenInclude(label => label.Category))
            .Where<int>((order, id) => order.Id == id).AsSplitQuery().ProjectTo(order => LoadingMapper.Labels(order)).List());
        var rows = await Invoke<Task<IReadOnlyList<LoadingLabelDto>>>(fixture.Operations(ClosedRepositoryTests.Compile(model)),
            "LabelsAsync", 1, CancellationToken.None);
        rows[0].Labels.Should().Equal("priority:important");
        model.Entities.Single().Queries.Single().Cache.DependentEntityTypeNames.Should()
            .Contain(RepositoryModelFactory.CSharpTypeName(typeof(LoadingLabelCategory)));
        fixture.Provider.Contexts.Should().ContainSingle().Which.TrackedAtDisposal.Should().Be(0);
    }

    [Fact]
    public void ExplicitLoadingAddsAllNestedCacheDependenciesWithoutExposingProfileMethods()
    {
        using var context = Context();
        var model = Model(context, builder =>
        {
            builder.Configure(options => options.GenerateCacheDecorator());
            ConfigureQueries(builder);
            builder.Entity<LoadingOrder>().HasQuery("CachedDetail").UseLoadProfile("Detail")
                .ProjectTo(order => LoadingMapper.Details(order)).Cache().List();
            builder.Entity<LoadingProduct>();
            builder.Entity<LoadingAddress>();
        }, closed: false);
        var owner = model.Entities.Single(entity => entity.EntityName == nameof(LoadingOrder));
        model.Entities.Single(entity => entity.EntityName == nameof(LoadingProduct)).CacheInvalidationTags.Should().Contain(owner.CacheTag);
        model.Entities.Single(entity => entity.EntityName == nameof(LoadingAddress)).CacheInvalidationTags.Should().Contain(owner.CacheTag);
        RepositorySourceRenderer.Render(model).Should().NotContain("public LoadingOrderCollectionSelector Detail(");
        ClosedRepositoryTests.Compile(model);
    }

    [Theory]
    [InlineData("scalar")]
    [InlineData("unmapped")]
    [InlineData("select")]
    [InlineData("materialization")]
    [InlineData("referenceFilter")]
    [InlineData("collectionMember")]
    [InlineData("captured")]
    [InlineData("conflictingFilter")]
    [InlineData("parameterizedDefault")]
    [InlineData("parameterizedProjection")]
    [InlineData("strategy")]
    public void InvalidLoadingPoliciesFailDuringModelValidation(string scenario)
    {
        using var context = Context();
        Action create = () => Model(context, builder =>
        {
            var entity = builder.Entity<LoadingOrder>();
            var query = entity.HasQuery("Details").ProjectTo(order => LoadingMapper.Details(order));
            if (scenario == "scalar")
            {
                query.Load(load => load.Include(order => order.Name));
            }

            if (scenario == "unmapped")
            {
                query.Load(load => load.Include(order => order.Unmapped));
            }

            if (scenario == "select")
            {
                query.Load(load => load.Include(order => order.Lines.Select(line => line.Product)));
            }

            if (scenario == "materialization")
            {
                query.Include(order => order.Lines.ToList());
            }

            if (scenario == "referenceFilter")
            {
                query.Load(load => load.Include(order => new[] { order.Customer }.Where(customer => customer != null)));
            }

            if (scenario == "collectionMember")
            {
                query.Load(load => load.Include(order => order.Lines).ThenInclude(lines => lines.Count));
            }

            if (scenario == "captured")
            {
                var minimum = Environment.TickCount;
                query.Load(load => load.Include(order => order.Lines.Where(line => line.Quantity > minimum)));
            }

            if (scenario == "conflictingFilter")
            {
                query.Load(load =>
                {
                    load.Include(order => order.Lines.Where(line => line.Quantity > 1));
                    load.Include(order => order.Lines.Where(line => line.Quantity > 5)).ThenInclude(line => line.Product);
                });
            }

            if (scenario is "parameterizedDefault" or "parameterizedProjection")
            {
                var profile = entity.HasLoadProfile<LoadingRequest>("Input", load => load.Include((order, request) => order.Lines.Take(request.MaximumLines)));
                if (scenario == "parameterizedDefault")
                {
                    entity.UseLoadProfile(profile);
                }
                else
                {
                    entity.HasProjection(order => LoadingMapper.Details(order)).UseLoadProfile(profile);
                }
            }

            if (scenario == "strategy")
            {
                query.Load(load =>
                {
                    load.Include(order => order.Lines);
                    load.Include(order => order.Notes);
                }).RequireExplicitLoadStrategy();
            }
        });
        create.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("stale")]
    [InlineData("from")]
    public void InvalidProfileReferencesAndStaleChainsFailDuringDesign(string scenario)
    {
        Action create = () =>
        {
            var builder = new RepositoryModelBuilder();
            var entity = builder.Entity<LoadingOrder>();
            if (scenario == "missing")
            {
                entity.HasQuery("Missing").UseLoadProfile("NoSuchProfile");
            }

            if (scenario == "duplicate")
            {
                entity.HasLoadProfile("Graph", _ => { });
                entity.HasLoadProfile("Graph", _ => { });
            }

            if (scenario == "foreign")
            {
                entity.HasQuery("Foreign").UseLoadProfile(new RepositoryModelBuilder().Entity<LoadingOrder>().HasLoadProfile("Other", _ => { }));
            }

            if (scenario == "stale")
            {
                entity.Load(load =>
                {
                    var chain = load.Include(order => order.Lines);
                    chain.ThenInclude(line => line.Product);
                    chain.ThenInclude(line => line.Tags);
                });
            }

            if (scenario == "from")
            {
                entity.HasQuery("WrongRoot").UseLoadProfile("Graph").From(sources => sources.Set<LoadingOrder>()).List();
            }

            builder.Build();
        };
        create.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ForwardProfileReferencesResolveIdempotentlyAndUnusedProfilesDoNotAlterDefaults()
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<LoadingOrder>();
        entity.HasQuery("Later").UseLoadProfile("Future").AsReadOnly().ProjectTo(order => LoadingMapper.LinesOnly(order));
        entity.HasLoadProfile("Future", load => load.Include(order => order.Lines).ThenInclude(line => line.Product));
        var first = builder.Build();
        var model = RepositoryModelFactory.Create(context, typeof(LoadingContext).Assembly, design: first);
        var source = RepositorySourceRenderer.Render(model);
        source.Should().Be(RepositorySourceRenderer.Render(RepositoryModelFactory.Create(context, typeof(LoadingContext).Assembly, design: builder.Build())));
        model.Entities.Single().Includes.Should().BeEmpty();
        model.Entities.Single().Queries.Single().Includes.Should().ContainSingle();
        model.Settings.GenerateTrackingMethods.Should().BeFalse();
    }

    private static void ConfigureQueries(RepositoryModelBuilder builder)
    {
        var entity = builder.Entity<LoadingOrder>();
        entity.HasLoadProfile("Detail", load =>
        {
            load.Include(order => order.Customer).ThenInclude(customer => customer!.Address);
            load.Include(order => order.Info);
            load.Include(order => order.Lines).ThenInclude(line => line.Product);
        });
        var filtered = entity.HasLoadProfile<LoadingRequest>("Filtered", load =>
        {
            load.Include((order, request) => order.Lines.Where(line => line.Quantity >= request.MinimumQuantity)
                    .OrderBy(line => line.Position))
                .ThenInclude((line, request) => line.Tags.Where(tag => tag.Tag.StartsWith(request.TagPrefix))
                    .OrderBy(tag => tag.Tag).Skip(0).Take(request.MaximumLines));
            load.Include((order, request) => order.Notes.Where(note => note.IsPublic || request.IncludePrivateNotes));
        });
        entity.HasQuery("Details").UseLoadProfile("Detail").UseLoadProfile(filtered)
            .Where<LoadingRequest>((order, request) => order.Id == request.Id).AsSplitQuery().RequireExplicitLoadStrategy()
            .ProjectTo(order => LoadingMapper.Details(order)).List();
        entity.HasQuery("SingleQueryDetails").UseLoadProfile("Detail").UseLoadProfile(filtered)
            .Where<LoadingRequest>((order, request) => order.Id == request.Id).AsSingleQuery().RequireExplicitLoadStrategy()
            .ProjectTo(order => LoadingMapper.Details(order)).List();
        entity.HasQuery("PageDetails").UseLoadProfile("Detail").UseLoadProfile(filtered)
            .Where<LoadingRequest>((order, request) => order.Id == request.Id).OrderBy(order => order.Id)
            .AsSplitQuery().ProjectTo(order => LoadingMapper.Details(order)).Paged();
        entity.HasQuery("StreamDetails").UseLoadProfile("Detail").Load(load =>
            {
                load.Include(order => order.Lines).ThenInclude(line => line.Tags);
                load.Include(order => order.Notes);
            })
            .OrderBy(order => order.Id).AsSplitQuery().ProjectTo(order => LoadingMapper.Details(order)).Stream();
    }

    private static RepositoryGenerationModel Model(LoadingContext context, Action<RepositoryModelBuilder> configure, bool closed = true)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        if (closed)
        {
            builder.Configure(options => options.UseClosedRepositories("Generated.Loading").UseAsyncContextLeases());
        }

        var entity = builder.Entity<LoadingOrder>();
        if (closed)
        {
            entity.WriteEnabled = false;
        }

        configure(builder);
        return RepositoryModelFactory.Create(context, typeof(LoadingContext).Assembly, design: builder.Build());
    }

    private static LoadingContext Context() => new(new DbContextOptionsBuilder<LoadingContext>().UseSqlite("Data Source=:memory:").Options);
    private static TResult Invoke<TResult>(object target, string name, params object[] arguments) => (TResult)target.GetType().GetMethod(name)!.Invoke(target, arguments)!;

    private sealed class Fixture(LoadingContext context, SqliteConnection connection, SqlRecorder sql) : IAsyncDisposable
    {
        public LoadingContext Context { get; } = context;
        public SqlRecorder Sql { get; } = sql;
        public LeaseProvider Provider { get; } = new(connection, sql);
        public object Operations(Assembly assembly) => Activator.CreateInstance(assembly.GetType(typeof(LoadingOrder).Namespace + ".LoadingOrderRepositoryOperations")!, Provider)!;

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var sql = new SqlRecorder();
            var context = new LoadingContext(new DbContextOptionsBuilder<LoadingContext>().UseSqlite(connection).AddInterceptors(sql).Options);
            await context.Database.EnsureCreatedAsync();
            context.Orders.AddRange(new LoadingOrder
            {
                Id = 1,
                Name = "First",
                Payload = "unused payload",
                Info = new LoadingInfo { Text = "owned metadata" },
                Customer = new LoadingCustomer { Name = "Ada", Address = new LoadingAddress { City = "Amsterdam" } },
                Lines =
                [
                    new LoadingLine { Position = 1, Quantity = 1, Product = new LoadingProduct { Name = "Water" }, Tags = [new LoadingLineTag { Tag = "public" }] },
                    new LoadingLine { Position = 2, Quantity = 5, Product = new LoadingProduct { Name = "Tea" }, Tags = [new LoadingLineTag { Tag = "public" }, new LoadingLineTag { Tag = "internal" }] },
                    new LoadingLine { Position = 3, Quantity = 9, Product = new LoadingProduct { Name = "Coffee" }, Tags = [new LoadingLineTag { Tag = "public" }] }
                ],
                Notes = [new LoadingNote { Text = "public", IsPublic = true }, new LoadingNote { Text = "private", IsPublic = false }],
                Labels = [new LoadingLabel { Name = "priority", Category = new LoadingLabelCategory { Name = "important" } }]
            }, new LoadingOrder { Id = 2, Name = "Second", Lines = [new LoadingLine { Position = 1, Quantity = 1, Product = new LoadingProduct { Name = "Other" } }] });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            sql.Commands.Clear();
            return new Fixture(context, connection, sql);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class LeaseProvider(SqliteConnection connection, SqlRecorder sql) : IDbContextLeaseProvider<LoadingContext>
    {
        public List<LoadingContext> Contexts { get; } = [];

        public ValueTask<DbContextLease<LoadingContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new LoadingContext(new DbContextOptionsBuilder<LoadingContext>().UseSqlite(connection).AddInterceptors(sql).Options);
            Contexts.Add(context);
            return ValueTask.FromResult(DbContextLease.Owned(context));
        }
    }

    private sealed class SqlRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}

public sealed record LoadingRequest(int Id, int MinimumQuantity, int MaximumLines, string TagPrefix, bool IncludePrivateNotes);

public sealed record LoadingLineDto(string Product, int Quantity, IReadOnlyList<string> Tags);

public sealed record LoadingOrderDto(int Id, string? Customer, string? City, string? Info, IReadOnlyList<LoadingLineDto> Lines, IReadOnlyList<string> Notes);

public sealed record LoadingLabelDto(int Id, IReadOnlyList<string> Labels);

public interface ILoadingReadPort
{
    Task<IReadOnlyList<LoadingOrderDto>> LoadByRequestAsync(LoadingRequest request, CancellationToken cancellationToken = default);
}

public static class LoadingMapper
{
    public static LoadingOrderDto Details(LoadingOrder order) => new(order.Id, order.Customer?.Name, order.Customer?.Address?.City, order.Info?.Text,
        order.Lines.OrderBy(line => line.Position).Select(line => new LoadingLineDto(line.Product?.Name ?? "", line.Quantity, line.Tags.Select(tag => tag.Tag).Order().ToArray())).ToArray(),
        order.Notes.Select(note => note.Text).Order().ToArray());

    public static LoadingOrderDto LinesOnly(LoadingOrder order) => new(order.Id, null, null, null,
        order.Lines.Select(line => new LoadingLineDto(line.Product?.Name ?? "", line.Quantity, [])).ToArray(), []);

    public static LoadingLabelDto Labels(LoadingOrder order) => new(order.Id,
        order.Labels.Select(label => label.Name + ":" + label.Category.Name).ToArray());
}

public sealed class LoadingOrder
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Payload { get; set; } = "";
    public int? CustomerId { get; set; }
    public LoadingCustomer? Customer { get; set; }
    public LoadingInfo? Info { get; set; }
    public List<LoadingLine> Lines { get; set; } = [];
    public ICollection<LoadingNote> Notes { get; set; } = new List<LoadingNote>();
    public ICollection<LoadingLabel> Labels { get; set; } = new List<LoadingLabel>();
    public LoadingCustomer? Unmapped { get; set; }
}

public sealed class LoadingCustomer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? AddressId { get; set; }
    public LoadingAddress? Address { get; set; }
}

public sealed class LoadingAddress
{
    public int Id { get; set; }
    public string City { get; set; } = "";
}

public sealed class LoadingProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class LoadingLine
{
    public int Id { get; set; }
    public int LoadingOrderId { get; set; }
    public int ProductId { get; set; }
    public LoadingProduct Product { get; set; } = null!;
    public int Position { get; set; }
    public int Quantity { get; set; }
    public ICollection<LoadingLineTag> Tags { get; set; } = new List<LoadingLineTag>();
}

public sealed class LoadingLineTag
{
    public int Id { get; set; }
    public int LoadingLineId { get; set; }
    public string Tag { get; set; } = "";
}

public sealed class LoadingNote
{
    public int Id { get; set; }
    public int LoadingOrderId { get; set; }
    public string Text { get; set; } = "";
    public bool IsPublic { get; set; }
}

public sealed class LoadingInfo
{
    public string Text { get; set; } = "";
}

public sealed class LoadingLabel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public LoadingLabelCategory Category { get; set; } = null!;
}

public sealed class LoadingLabelCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class LoadingContext(DbContextOptions<LoadingContext> options) : DbContext(options)
{
    public DbSet<LoadingOrder> Orders => Set<LoadingOrder>();
    public int DisposeCount { get; private set; }
    public int TrackedAtDisposal { get; private set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var order = modelBuilder.Entity<LoadingOrder>();
        order.Ignore(row => row.Unmapped);
        order.HasMany(row => row.Lines).WithOne().HasForeignKey(row => row.LoadingOrderId);
        order.HasMany(row => row.Notes).WithOne().HasForeignKey(row => row.LoadingOrderId);
        order.OwnsOne(row => row.Info);
        order.HasMany(row => row.Labels).WithMany();
        modelBuilder.Entity<LoadingLine>().HasMany(row => row.Tags).WithOne().HasForeignKey(row => row.LoadingLineId);
    }

    public override ValueTask DisposeAsync()
    {
        DisposeCount++;
        TrackedAtDisposal = ChangeTracker.Entries().Count();
        return base.DisposeAsync();
    }
}