using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class TrackingConfigurationTests
{
    [Fact]
    public void CompatibilityPreservesExistingNamesAndSimpleOmitsTrackingControls()
    {
        using var context = Context();
        var compatibility = ClosedRepositoryTests.Compile(Model(context, static _ => { }));
        foreach (var suffix in new[] { "Selector", "CollectionSelector" })
        {
            var type = GeneratedType(compatibility, "ClosedRow" + suffix);
            type.GetMethod("AsNoTracking").Should().NotBeNull();
            type.GetMethod("AsTracking").Should().NotBeNull();
            type.GetMethod("AsNoTrackingWithIdentityResolution").Should().NotBeNull();
        }

        var simple = Model(context, options => options.UseSimpleDefaults());
        var assembly = ClosedRepositoryTests.Compile(simple);
        foreach (var suffix in new[] { "Selector", "CollectionSelector" })
        {
            var type = GeneratedType(assembly, "ClosedRow" + suffix);
            type.GetMethod("AsNoTracking").Should().BeNull();
            type.GetMethod("AsTracking").Should().BeNull();
            type.GetMethod("AsNoTrackingWithIdentityResolution").Should().BeNull();
        }

        RepositoryManifestRenderer.Render(simple).Should().Contain("\"selectorTrackingMethods\": false");
    }

    [Fact]
    public async Task RenamedControlsWorkOnBothSelectorsAndPreserveIdentityAndWriteCallbacks()
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, options => options.UseSimpleDefaults().GenerateTrackingMethods()
            .UseTrackingMethodNames("AsReadOnly", "AsEditable", "AsReadOnlyWithSharedIdentity"));
        var assembly = ClosedRepositoryTests.Compile(model);
        foreach (var suffix in new[] { "Selector", "CollectionSelector" })
        {
            var type = GeneratedType(assembly, "ClosedRow" + suffix);
            type.GetMethod("AsReadOnly").Should().NotBeNull();
            type.GetMethod("AsEditable").Should().NotBeNull();
            type.GetMethod("AsReadOnlyWithSharedIdentity").Should().NotBeNull();
            type.GetMethod("AsNoTracking").Should().BeNull();
            type.GetMethod("AsTracking").Should().BeNull();
            type.GetMethod("AsNoTrackingWithIdentityResolution").Should().BeNull();
        }

        var repository = Repository(assembly, context);
        var single = Invoke<object>(Invoke<object>(repository, "ById", 1), "AsReadOnly");
        single.GetType().Should().Be(GeneratedType(assembly, "ClosedRowSelector"));
        (await EntityAsync(assembly, single))!.Name.Should().Be("Ada");
        context.ChangeTracker.Entries().Should().BeEmpty();
        await EntityAsync(assembly, Invoke<object>(single, "AsEditable"));
        context.ChangeTracker.Entries().Should().ContainSingle();
        context.ChangeTracker.Clear();

        var query = context.Rows.Concat(context.Rows);
        var collectionType = GeneratedType(assembly, "ClosedRowCollectionSelector");
        var collection = collectionType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(DbContext), typeof(IQueryable<ClosedRow>)])!.Invoke([context, query]);
        var repeated = await EntitiesAsync(assembly, Invoke<object>(collection, "AsReadOnly"));
        repeated[0].Should().NotBeSameAs(repeated[2]);
        context.ChangeTracker.Entries().Should().BeEmpty();
        var resolved = await EntitiesAsync(assembly, Invoke<object>(collection, "AsReadOnlyWithSharedIdentity"));
        resolved[0].Should().BeSameAs(resolved[2]);
        context.ChangeTracker.Entries().Should().BeEmpty();

        var invalidations = 0;
        Func<CancellationToken, Task> invalidate = _ =>
        {
            invalidations++;
            return Task.CompletedTask;
        };
        var withCallback = GeneratedType(assembly, "ClosedRowSelector").GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                [typeof(DbContext), typeof(IQueryable<ClosedRow>), typeof(Func<CancellationToken, Task>)])!
            .Invoke([context, context.Rows.Where(row => row.Id == 1), invalidate]);
        var readOnly = Invoke<object>(withCallback, "AsReadOnly");
        (await Invoke<Task<bool>>(readOnly, "DeleteAsync", CancellationToken.None)).Should().BeTrue();
        invalidations.Should().Be(1);
        (await context.Rows.CountAsync()).Should().Be(1);
        RepositoryManifestRenderer.Render(model).Should().Contain("\"noTrackingMethod\": \"AsReadOnly\"");
    }

    [Fact]
    public async Task HiddenControlsDoNotDisableDesignerPoliciesOrDefaultReadAndWriteBehavior()
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, options => options.UseSimpleDefaults().UseDefaultTracking(RepositoryTrackingBehavior.NoTracking),
            entity =>
            {
                entity.HasQuery("ReadRows").AsReadOnly().List();
                entity.HasQuery("ReadRowsWithIdentity").AsReadOnly(identityResolution: true).List();
                entity.HasQuery("EditableRows").AsTracking().List();
            });
        var assembly = ClosedRepositoryTests.Compile(model);
        var repository = Repository(assembly, context);
        (await EntitiesAsync(assembly, Invoke<object>(repository, "GetAll"))).Should().HaveCount(2);
        await EntityAsync(assembly, Invoke<object>(repository, "ById", 1));
        context.ChangeTracker.Entries().Should().BeEmpty();
        await LegacyQuery<Task<IReadOnlyList<ClosedRow>>>(assembly, repository, "ReadRowsAsync");
        await LegacyQuery<Task<IReadOnlyList<ClosedRow>>>(assembly, repository, "ReadRowsWithIdentityAsync");
        context.ChangeTracker.Entries().Should().BeEmpty();
        await LegacyQuery<Task<IReadOnlyList<ClosedRow>>>(assembly, repository, "EditableRowsAsync");
        context.ChangeTracker.Entries().Should().HaveCount(2);
        context.ChangeTracker.Clear();

        var single = Invoke<object>(repository, "ById", 1);
        Action<ClosedRow> update = row => row.Name = "Updated";
        (await Invoke<Task<ClosedRow?>>(single, "UpdateAsync", update, CancellationToken.None))!.Name.Should().Be("Updated");
        (await context.Rows.AsNoTracking().SingleAsync(row => row.Id == 1)).Name.Should().Be("Updated");
    }

    [Fact]
    public void IndividualMethodsCanBeOmittedWithoutSuppressingOtherControls()
    {
        using var context = Context();
        var model = Model(context, options => options.UseSimpleDefaults().GenerateTrackingMethods()
            .UseTrackingMethodNames(noTracking: "AsReadOnly", tracking: null, noTrackingWithIdentityResolution: null));
        var assembly = ClosedRepositoryTests.Compile(model);
        foreach (var suffix in new[] { "Selector", "CollectionSelector" })
        {
            var type = GeneratedType(assembly, "ClosedRow" + suffix);
            type.GetMethod("AsReadOnly").Should().NotBeNull();
            type.GetMethod("AsTracking").Should().BeNull();
            type.GetMethod("AsNoTrackingWithIdentityResolution").Should().BeNull();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadContractsDefaultToNoTrackingRegardlessOfSelectorOrContextDefaults(bool asyncLeases)
    {
        await using var context = Context();
        await SeedAsync(context);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        var model = ReadModel(context, options => options.UseDefaultTracking(RepositoryTrackingBehavior.Tracking), asyncLeases);
        model.Entities.Single().Queries.Should().OnlyContain(query => query.TrackingBehavior == RepositoryTrackingBehavior.NoTracking);
        var assembly = ClosedRepositoryTests.Compile(model);
        var readPort = assembly.GetType("Generated.Tracking.IReadClosedRowRepository")!;
        readPort.GetMethods().Should().NotContain(method => method.Name.StartsWith("As", StringComparison.Ordinal));
        assembly.GetType("Generated.Tracking.IWriteClosedRowRepository").Should().BeNull();
        var provider = new BorrowedProvider(context);
        var operations = Operations(assembly, context, provider, asyncLeases);

        // These top-level client projections deliberately materialize an entity. A DTO-only scalar
        // projection would not prove the tracking policy, because it never materializes an entity.
        (await Invoke<Task<IReadOnlyList<string>>>(operations, "NamesAsync", CancellationToken.None)).Should().HaveCount(2);
        (await Invoke<Task<IReadOnlyList<string>>>(operations, "NamesFromAsync", CancellationToken.None)).Should().HaveCount(2);
        (await Invoke<Task<string?>>(operations, "FirstNameAsync", CancellationToken.None)).Should().Be("Ada");
        (await Invoke<Task<PagedResult<string>>>(operations, "PageNamesAsync", 1, 1, CancellationToken.None)).Items.Should().ContainSingle();
        await foreach (var _ in Invoke<IAsyncEnumerable<string>>(operations, "StreamNamesAsync", CancellationToken.None)) { }

        context.ChangeTracker.Entries().Should().BeEmpty();
        context.DisposeCount.Should().Be(0);
        provider.Acquisitions.Should().Be(asyncLeases ? 5 : 0);
    }

    [Theory]
    [InlineData(RepositoryTrackingBehavior.NoTracking, 0)]
    [InlineData(RepositoryTrackingBehavior.NoTrackingWithIdentityResolution, 0)]
    [InlineData(RepositoryTrackingBehavior.Tracking, 2)]
    [InlineData(RepositoryTrackingBehavior.Unspecified, 2)]
    public async Task ReadContractDefaultsAreConfigurableAndExplicitQueryPoliciesTakePrecedence(RepositoryTrackingBehavior behavior, int trackedCount)
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = ReadModel(context, options => options.UseDefaultTracking(RepositoryTrackingBehavior.Tracking)
            .UseReadOnlyTracking(behavior), asyncLeases: false, explicitQueries: true);
        var operations = Operations(ClosedRepositoryTests.Compile(model), context, new BorrowedProvider(context), asyncLeases: false);
        await Invoke<Task<IReadOnlyList<string>>>(operations, "NamesAsync", CancellationToken.None);
        context.ChangeTracker.Entries().Should().HaveCount(trackedCount);
        context.ChangeTracker.Clear();
        await Invoke<Task<IReadOnlyList<string>>>(operations, "NamesFromAsync", CancellationToken.None);
        context.ChangeTracker.Entries().Should().HaveCount(trackedCount);
        context.ChangeTracker.Clear();
        await Invoke<Task<IReadOnlyList<string>>>(operations, "EditableNamesAsync", CancellationToken.None);
        context.ChangeTracker.Entries().Should().HaveCount(2);
        context.ChangeTracker.Clear();
        await Invoke<Task<IReadOnlyList<string>>>(operations, "ReadOnlyNamesAsync", CancellationToken.None);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ReadOnlyOperationsPreserveExistingTrackedChangesAndSharedUnitOfWorkSaves()
    {
        await using var context = Context();
        await SeedAsync(context);
        var original = await context.Rows.SingleAsync(row => row.Id == 1);
        original.Name = "Pending";
        await using var work = new RepositoryUnitOfWork<ClosedContext>(context);
        var model = ReadModel(context, options => options.UseDefaultTracking(RepositoryTrackingBehavior.Tracking), asyncLeases: true);
        var assembly = ClosedRepositoryTests.Compile(model);
        var operations = Activator.CreateInstance(GeneratedType(assembly, "ClosedRowRepositoryOperations"), work)!;
        (await Invoke<Task<IReadOnlyList<string>>>(operations, "NamesAsync", CancellationToken.None)).Should().Contain("Ada").And.NotContain("Pending");
        context.ChangeTracker.Entries<ClosedRow>().Should().ContainSingle().Which.Entity.Should().BeSameAs(original);
        context.Entry(original).State.Should().Be(EntityState.Modified);
        (await work.SaveChangesAsync()).Should().Be(1);
        (await context.Rows.AsNoTracking().SingleAsync(row => row.Id == 1)).Name.Should().Be("Pending");
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad name")]
    [InlineData("class")]
    [InlineData("__arglist")]
    [InlineData("1ReadOnly")]
    [InlineData("ReadOnly() {}")]
    [InlineData("Where")]
    [InlineData("Delete")]
    [InlineData("ConfigureQuery")]
    [InlineData("ToString")]
    [InlineData("ClosedRowSelector")]
    [InlineData("ClosedRowCollectionSelector")]
    [InlineData("ProjectToName")]
    public void InvalidOrCollidingMethodNamesFailBeforeRendering(string name)
    {
        using var context = Context();
        Action create = () => Model(context, options => options.UseTrackingMethodNames(noTracking: name),
            entity => entity.HasProjection<string>("ProjectToName", row => row.Name));
        create.Should().Throw<InvalidOperationException>().WithMessage("*Tracking method name*");
    }

    [Fact]
    public void DuplicateNamesAndUnknownTrackingModesAreRejected()
    {
        using var context = Context();
        Action duplicate = () => Model(context, options => options.UseTrackingMethodNames("AsReadOnly", "AsReadOnly"));
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate tracking method name*");
        Action invalidDefault = () => new RepositoryModelBuilder().Configure(options => options.UseDefaultTracking((RepositoryTrackingBehavior)42));
        Action invalidReadOnly = () => new RepositoryModelBuilder().Configure(options => options.UseReadOnlyTracking((RepositoryTrackingBehavior)(-1)));
        invalidDefault.Should().Throw<ArgumentOutOfRangeException>();
        invalidReadOnly.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExistingSelectorsCannotSilentlyIgnoreHiddenOrRenamedTrackingControls(bool hidden)
    {
        using var context = new ExistingTrackingContext(new DbContextOptionsBuilder<ExistingTrackingContext>().UseInMemoryDatabase("tracking-reuse").Options);
        var builder = new RepositoryModelBuilder().Configure(options =>
        {
            if (hidden)
            {
                options.GenerateTrackingMethods(false);
            }
            else
            {
                options.UseTrackingMethodNames(noTracking: "AsReadOnly");
            }
        });
        Action create = () => RepositoryModelFactory.Create(context, typeof(ExistingTrackingContext).Assembly, design: builder.Build());
        create.Should().Throw<InvalidOperationException>().WithMessage("*reuses existing selectors*--generate-selectors*");
        RepositoryModelFactory.Create(context, typeof(ExistingTrackingContext).Assembly, generateSelectors: true, design: builder.Build())
            .Entities.Single().GenerateSelectors.Should().BeTrue();
    }

    [Fact]
    public void AttributeSelectorsRequireAnExplicitSwitchToDesignerGeneratedSelectors()
    {
        using var context = new AttributeTrackingContext(new DbContextOptionsBuilder<AttributeTrackingContext>().UseInMemoryDatabase("tracking-attribute").Options);
        var design = new RepositoryModelBuilder().Configure(options => options.UseTrackingMethodNames(noTracking: "AsReadOnly")).Build();
        Action create = () => RepositoryModelFactory.Create(context, typeof(AttributeTrackingContext).Assembly, generateSelectors: true, design: design);
        create.Should().Throw<InvalidOperationException>().WithMessage("*attribute-generated selectors*Remove GenerateSelectors*");
    }

    private static RepositoryGenerationModel Model(ClosedContext context, Action<RepositoryGenerationOptionsBuilder> configure,
        Action<RepositoryEntityBuilder<ClosedRow>>? configureEntity = null)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.GenerateOnlyConfiguredEntities());
        builder.Configure(configure);
        var entity = builder.Entity<ClosedRow>();
        configureEntity?.Invoke(entity);
        return RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: builder.Build());
    }

    private static RepositoryGenerationModel ReadModel(ClosedContext context, Action<RepositoryGenerationOptionsBuilder> configure,
        bool asyncLeases, bool explicitQueries = false) => Model(context,
        options =>
        {
            options.UseSimpleDefaults().UseClosedRepositories("Generated.Tracking").UseAsyncContextLeases(asyncLeases);
            configure(options);
        },
        entity =>
        {
            entity.WriteEnabled = false;
            entity.HasQuery("Names").ProjectTo(row => TrackingProjection.ReadName(row)).List();
            entity.HasQuery("NamesFrom").From(sources => sources.Set<ClosedRow>().Select(row => TrackingProjection.ReadName(row))).List();
            entity.HasQuery("FirstName").OrderBy(row => row.Id).ProjectTo(row => TrackingProjection.ReadName(row)).FirstOrDefault();
            entity.HasQuery("PageNames").OrderBy(row => row.Id).ProjectTo(row => TrackingProjection.ReadName(row)).Paged();
            entity.HasQuery("StreamNames").ProjectTo(row => TrackingProjection.ReadName(row)).Stream();
            if (!explicitQueries)
            {
                return;
            }

            entity.HasQuery("EditableNames").AsTracking().ProjectTo(row => TrackingProjection.ReadName(row)).List();
            entity.HasQuery("ReadOnlyNames").AsReadOnly(identityResolution: true).ProjectTo(row => TrackingProjection.ReadName(row)).List();
        });

    private static ClosedContext Context() => new(new DbContextOptionsBuilder<ClosedContext>().UseSqlite("Data Source=:memory:").Options);

    private static async Task SeedAsync(ClosedContext context)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.Rows.AddRange(new ClosedRow { Id = 1, Name = "Ada" }, new ClosedRow { Id = 2, Name = "Ron" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static Type GeneratedType(Assembly assembly, string name) => assembly.GetType(typeof(ClosedRow).Namespace + "." + name)!;
    private static object Repository(Assembly assembly, ClosedContext context) => Activator.CreateInstance(GeneratedType(assembly, "ClosedRowRepository"), context)!;

    private static object Operations(Assembly assembly, ClosedContext context, BorrowedProvider provider, bool asyncLeases) =>
        Activator.CreateInstance(GeneratedType(assembly, "ClosedRowRepositoryOperations"), asyncLeases ? provider : context)!;

    private static TResult Invoke<TResult>(object target, string name, params object[] arguments) =>
        (TResult)target.GetType().GetMethod(name)!.Invoke(target, arguments)!;

    private static Task<ClosedRow?> EntityAsync(Assembly assembly, object selector) =>
        (Task<ClosedRow?>)GeneratedType(assembly, "ClosedRowSelectorExtensions").GetMethod("AsEntityAsync")!.Invoke(null, [selector, CancellationToken.None])!;

    private static Task<IReadOnlyList<ClosedRow>> EntitiesAsync(Assembly assembly, object selector) =>
        (Task<IReadOnlyList<ClosedRow>>)GeneratedType(assembly, "ClosedRowCollectionSelectorExtensions").GetMethod("AsEntitiesAsync")!.Invoke(null, [selector, CancellationToken.None])!;

    private static TResult LegacyQuery<TResult>(Assembly assembly, object repository, string name) =>
        (TResult)GeneratedType(assembly, "ClosedRowConfiguredQueryExtensions").GetMethod(name)!.Invoke(null, [repository, CancellationToken.None])!;

    private sealed class BorrowedProvider(ClosedContext context) : IDbContextLeaseProvider<ClosedContext>
    {
        public int Acquisitions { get; private set; }

        public ValueTask<DbContextLease<ClosedContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Acquisitions++;
            return ValueTask.FromResult(DbContextLease.Borrowed(context));
        }
    }
}

public static class TrackingProjection
{
    public static string ReadName(ClosedRow row) => row.Name;
}

public sealed class ExistingTrackingRow
{
    public int Id { get; set; }
}

public static class ExistingTrackingRowSelector { }

public sealed class ExistingTrackingContext(DbContextOptions<ExistingTrackingContext> options) : DbContext(options)
{
    public DbSet<ExistingTrackingRow> Rows => Set<ExistingTrackingRow>();
}

[global::RonSijm.RepoGen.GenerateSelectors]
public sealed class AttributeTrackingRow
{
    public int Id { get; set; }
}

public static class AttributeTrackingRowSelector { }

public sealed class AttributeTrackingContext(DbContextOptions<AttributeTrackingContext> options) : DbContext(options)
{
    public DbSet<AttributeTrackingRow> Rows => Set<AttributeTrackingRow>();
}