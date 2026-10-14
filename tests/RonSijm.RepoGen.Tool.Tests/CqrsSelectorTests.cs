using System.Linq.Expressions;
using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CqrsSelectorTests
{
    [Fact]
    public void ReadSurfaceDoesNotInheritWritesOrExposeQueryEscapes()
    {
        using var context = Context();
        var model = Model(context, options => options.GenerateTrackingMethods().GenerateSetBasedOperations());
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        TypeOf(assembly, "IClosedRowRepository").IsPublic.Should().BeFalse();
        TypeOf(assembly, "ICacheableClosedRowRepository", required: false).Should().BeNull();
        foreach (var suffix in new[] { "Selector", "CollectionSelector" })
        {
            var type = TypeOf(assembly, "ReadClosedRow" + suffix);
            type.IsSealed.Should().BeTrue();
            type.BaseType.Should().Be<object>();
            type.GetMethods().Should().NotContain(method => new[]
                { "Delete", "DeleteAsync", "Remove", "MarkForDeletionAsync", "Update", "UpdateAsync", "SaveChangesAsync", "ExecuteDeleteAsync", "ExecuteUpdateAsync", "ConfigureQuery" }.Contains(method.Name));
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty();
            type.GetMethods().Where(static method => method.Name is "Where" or "Include" or "OrderBy" or "Skip" or "Take" or "AsReadOnly")
                .Should().OnlyContain(method => method.ReturnType == type);
        }

        TypeOf(assembly, "IReadClosedRowRepository").GetMethods().Should().NotContain(method => method.Name.StartsWith("Save", StringComparison.Ordinal) || method.Name == "DeleteNamedAsync");
        TypeOf(assembly, "IWriteClosedRowRepository").GetMethods().Should().Contain(method => method.Name == "SaveChangesAsync");
        RepositoryManifestRenderer.Render(model).Should().Contain("\"surface\": \"cqrsSelectors\"");
    }

    [Theory]
    [InlineData("read.ById(1).Delete();")]
    [InlineData("read.ById(1).MarkForDeletionAsync();")]
    [InlineData("read.ById(1).Where(row => true).Update(row => row.Name = \"bad\");")]
    [InlineData("read.GetAll().Where(row => true).OrderBy(row => row.Id).Take(1).ExecuteDeleteAsync();")]
    [InlineData("read.GetAll().ConfigureQuery(query => query);")]
    [InlineData("read.SaveChangesAsync();")]
    [InlineData("var selector = (global::RonSijm.RepoGen.BaseEntitySelector<ClosedRow>)read.ById(1);")]
    public void ReadConsumersCannotCompileMutationOrEscapePaths(string statement)
    {
        using var context = Context();
        var errors = CompileErrors(Model(context, options => options.GenerateSetBasedOperations()), $$"""
                                                                                                      namespace RonSijm.RepoGen.Tool.Tests;
                                                                                                      public static class Consumer
                                                                                                      {
                                                                                                          public static void Use(IReadClosedRowRepository read) { {{statement}} }
                                                                                                      }
                                                                                                      """);
        errors.Should().Contain(diagnostic => diagnostic.Id == "CS1061" || diagnostic.Id == "CS0030");
    }

    [Fact]
    public void PublicPortsCompileForAnExternalConsumerButCombinedPersistenceIsInternal()
    {
        using var context = Context();
        var model = Model(context);
        CompileErrors(model, """
                             using System.Threading;
                             using System.Threading.Tasks;
                             using RonSijm.RepoGen.Tool.Tests;
                             public static class Consumer
                             {
                                 public static async Task Use(IReadClosedRowRepository read, IWriteClosedRowRepository write, CancellationToken token)
                                 {
                                     await read.ById(1).AsEntityAsync(token);
                                     await read.GetAll().OrderBy(row => row.Id).ToPagedListAsync(1, 20, token);
                                     await write.ById(1).Delete().SaveChangesAsync(token);
                                 }
                             }
                             """).Should().BeEmpty();
        CompileErrors(model, "public class Consumer { private RonSijm.RepoGen.Tool.Tests.IClosedRowRepository? source; }")
            .Should().Contain(diagnostic => diagnostic.Id == "CS0122");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAndWritePortsShareBorrowedContextWithoutTrackingReadEntities(bool resolver)
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, options => options.UseDbContextResolver(resolver).UseDefaultTracking(RepositoryTrackingBehavior.Tracking));
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        await using var provider = Provider(assembly, context, resolver);
        var read = provider.GetRequiredService(TypeOf(assembly, "IReadClosedRowRepository"));
        var write = provider.GetRequiredService(TypeOf(assembly, "IWriteClosedRowRepository"));
        var item = await Invoke<Task<ClosedRow?>>(Invoke<object>(read, "ById", 1), "AsEntityAsync", CancellationToken.None);
        item!.Name.Should().Be("Ada");
        context.ChangeTracker.Entries().Should().BeEmpty();
        item.Name = "Detached edit";
        var ordered = Invoke<object>(Invoke<object>(read, "GetAllUnordered"), "OrderBy", "id", true);
        (await Invoke<Task<ClosedRow?>>(ordered, "FirstOrDefaultAsync", CancellationToken.None))!.Id.Should().Be(2);
        await Invoke<Task>(write, "AddAsync", new ClosedRow { Id = 3, Name = "New" }, CancellationToken.None);
        context.Others.Add(new ClosedOther { Id = 4 });
        (await Invoke<Task<int>>(write, "SaveChangesAsync", CancellationToken.None)).Should().Be(2);
        (await Invoke<Task<ClosedRow?>>(Invoke<object>(read, "ById", 1), "AsEntityAsync", CancellationToken.None))!.Name.Should().Be("Ada");
        (await Invoke<Task<int>>(Invoke<object>(read, "GetAll"), "CountAsync", CancellationToken.None)).Should().Be(3);
        context.DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task ConfiguredQueriesAndProjectionsApplyReadPoliciesWithHiddenTrackingControls()
    {
        await using var context = Context();
        await SeedAsync(context);
        var assembly = ClosedRepositoryTests.Compile(Model(context, options => options.UseDefaultTracking(RepositoryTrackingBehavior.Tracking), entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasProjection<ClosedRowDto>(row => new ClosedRowDto(row.Id, row.Name));
            entity.HasQuery("Rows").List();
            entity.HasQuery("FromRows").From(sources => sources.Set<ClosedRow>()).List();
            entity.HasQuery("EditableRows").AsTracking().List();
            entity.HasQuery("Dtos").ProjectTo<ClosedRowDto>().List();
        }), rejectWarnings: true);
        await using var provider = Provider(assembly, context);
        var read = provider.GetRequiredService(TypeOf(assembly, "IReadClosedRowRepository"));
        var single = Invoke<object>(read, "ById", 1);
        single.GetType().GetMethod("ProjectToAsync").Should().BeNull();
        (await Invoke<Task<ClosedRowDto?>>(single, "ProjectToClosedRowDto", CancellationToken.None))!.Name.Should().Be("Ada");
        var collection = Invoke<object>(read, "GetAll");
        collection.GetType().GetMethod("ProjectToListAsync").Should().BeNull();
        collection.GetType().GetMethod("GroupBy").Should().BeNull();
        collection.GetType().GetMethod("AsNoTracking").Should().BeNull();
        (await Invoke<Task<IReadOnlyList<ClosedRowDto>>>(collection, "ProjectToClosedRowDto", CancellationToken.None)).Should().HaveCount(2);
        (await Invoke<Task<IReadOnlyList<ClosedRow>>>(read, "RowsAsync", CancellationToken.None)).Should().HaveCount(2);
        (await Invoke<Task<IReadOnlyList<ClosedRow>>>(read, "FromRowsAsync", CancellationToken.None)).Should().HaveCount(2);
        (await Invoke<Task<IReadOnlyList<ClosedRowDto>>>(read, "DtosAsync", CancellationToken.None)).Should().HaveCount(2);
        context.ChangeTracker.Entries().Should().BeEmpty();
        await Invoke<Task<IReadOnlyList<ClosedRow>>>(read, "EditableRowsAsync", CancellationToken.None);
        context.ChangeTracker.Entries().Should().HaveCount(2);
    }

    [Theory]
    [InlineData(RepositoryTrackingBehavior.Unspecified, 2)]
    [InlineData(RepositoryTrackingBehavior.Tracking, 2)]
    [InlineData(RepositoryTrackingBehavior.NoTracking, 0)]
    [InlineData(RepositoryTrackingBehavior.NoTrackingWithIdentityResolution, 0)]
    public async Task ReadDefaultsAreIndependentAndConfigurable(RepositoryTrackingBehavior tracking, int expected)
    {
        await using var context = Context();
        await SeedAsync(context);
        var assembly = ClosedRepositoryTests.Compile(Model(context, options => options.UseDefaultTracking(RepositoryTrackingBehavior.Tracking).UseReadOnlyTracking(tracking),
            entity => entity.HasQuery("Rows").List()), rejectWarnings: true);
        await using var provider = Provider(assembly, context);
        var read = provider.GetRequiredService(TypeOf(assembly, "IReadClosedRowRepository"));
        await Invoke<Task<IReadOnlyList<ClosedRow>>>(Invoke<object>(read, "GetAll"), "AsEntitiesAsync", CancellationToken.None);
        context.ChangeTracker.Entries().Should().HaveCount(expected);
        context.ChangeTracker.Clear();
        await Invoke<Task<IReadOnlyList<ClosedRow>>>(read, "RowsAsync", CancellationToken.None);
        context.ChangeTracker.Entries().Should().HaveCount(expected);
    }

    [Fact]
    public async Task ReadChainsRetainPagingDictionariesGroupingAggregatesAndStreaming()
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, configure: entity =>
        {
            entity.HasFilter<string>("Named", (row, name) => row.Name == name);
            entity.HasProjection<string>("ProjectName", row => row.Name);
            entity.HasQuery("Groups").GroupBy(row => row.Name, group => new ClosedRowDto(group.Count(), group.Key)).List();
        });
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        await using var provider = Provider(assembly, context);
        var read = provider.GetRequiredService(TypeOf(assembly, "IReadClosedRowRepository"));
        var rows = Invoke<object>(read, "GetAll");
        var page = await ResultAsync(Invoke<object>(rows, "ToPagedListAsync", 2, 1, CancellationToken.None));
        ((IReadOnlyList<ClosedRow>)Property(page, "Items")).Single().Name.Should().Be("Zoe");
        Property(page, "TotalCount").Should().Be(2);
        var dictionary = await (Task<Dictionary<int, ClosedRow>>)Generic(rows, "ToDictionaryAsync", [typeof(int)],
            (Func<ClosedRow, int>)(row => row.Id), CancellationToken.None);
        dictionary.Keys.Should().BeEquivalentTo([1, 2]);
        (await Invoke<Task<IReadOnlyList<string>>>(Invoke<object>(read, "Named", "Ada"), "ProjectName", CancellationToken.None)).Should().Equal("Ada");
        var group = Generic(rows, "GroupBy", [typeof(string)], (Expression<Func<ClosedRow, string>>)(row => row.Name));
        var counts = await (Task<IReadOnlyList<ClosedRowDto>>)Generic(group, "ProjectToListAsync", [typeof(ClosedRowDto)],
            (Expression<Func<IGrouping<string, ClosedRow>, ClosedRowDto>>)(g => new ClosedRowDto(g.Count(), g.Key)), CancellationToken.None);
        counts.Should().BeEquivalentTo([new ClosedRowDto(1, "Ada"), new ClosedRowDto(1, "Zoe")]);
        (await Invoke<Task<IReadOnlyList<ClosedRowDto>>>(read, "GroupsAsync", CancellationToken.None)).Should().BeEquivalentTo(counts);
        var maximum = await (Task<int>)Generic(rows, "MaxAsync", [typeof(int)], (Expression<Func<ClosedRow, int>>)(row => row.Id), CancellationToken.None);
        maximum.Should().Be(2);
        var stream = Invoke<IAsyncEnumerable<ClosedRow>>(rows, "StreamAsync", CancellationToken.None);
        var names = new List<string>();
        await foreach (var row in stream)
        {
            names.Add(row.Name);
        }

        names.Should().Equal("Ada", "Zoe");
        context.ChangeTracker.Entries().Should().BeEmpty();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await using var enumerator = Invoke<IAsyncEnumerable<ClosedRow>>(rows, "StreamAsync", canceled.Token).GetAsyncEnumerator();
        Func<Task> move = async () => { await enumerator.MoveNextAsync(); };
        await move.Should().ThrowAsync<OperationCanceledException>();
        context.DisposeCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdentityResolutionAndRenamedTrackingControlsKeepReadCapabilities(bool identityResolution)
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, options => options.UseReadOnlyTracking(identityResolution ? RepositoryTrackingBehavior.NoTrackingWithIdentityResolution : RepositoryTrackingBehavior.NoTracking)
            .GenerateTrackingMethods().UseTrackingMethodNames("AsReadOnly", null, "AsReadOnlyWithIdentity"), entity =>
            entity.HasQuery("Duplicates").From(sources => sources.Set<ClosedRow>().Concat(sources.Set<ClosedRow>())).List());
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        await using var provider = Provider(assembly, context);
        var read = provider.GetRequiredService(TypeOf(assembly, "IReadClosedRowRepository"));
        var duplicates = await Invoke<Task<IReadOnlyList<ClosedRow>>>(read, "DuplicatesAsync", CancellationToken.None);
        ReferenceEquals(duplicates[0], duplicates[2]).Should().Be(identityResolution);
        var single = Invoke<object>(Invoke<object>(read, "ById", 1), "AsReadOnly");
        single.GetType().Should().Be(TypeOf(assembly, "ReadClosedRowSelector"));
        single.GetType().GetMethod("Delete").Should().BeNull();
        single.GetType().GetMethod("AsTracking").Should().BeNull();
        await Invoke<Task<ClosedRow?>>(single, "AsEntityAsync", CancellationToken.None);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task OptionalDecoratorCachesNamedReadsAndInvalidatesFluentWrites()
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, options => options.GenerateCacheDecorator(), entity =>
            entity.HasQuery("Names").ProjectTo(row => row.Name).Cache().List());
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        // Merely generating a decorator must not require a provider for the default registration.
        await using (var plain = Provider(assembly, context))
        {
            await Invoke<Task<IReadOnlyList<string>>>(plain.GetRequiredService(TypeOf(assembly, "IReadClosedRowRepository")), "NamesAsync", CancellationToken.None);
        }

        var cache = DispatchProxy.Create(assembly.GetType("RonSijm.RepoGen.IRepositoryCacheProvider")!, typeof(CqrsCacheProxy));
        await using var provider = Provider(assembly, context, cache: cache);
        var read = provider.GetRequiredService(TypeOf(assembly, "IReadClosedRowRepository"));
        var write = provider.GetRequiredService(TypeOf(assembly, "IWriteClosedRowRepository"));
        (await Invoke<Task<IReadOnlyList<string>>>(read, "NamesAsync", CancellationToken.None)).Should().Equal("Ada", "Zoe");
        await Invoke<Task<IReadOnlyList<string>>>(read, "NamesAsync", CancellationToken.None);
        ((CqrsCacheProxy)cache).FactoryCalls.Should().Be(1);
        var operation = Invoke<object>(Invoke<object>(write, "ById", 1), "Delete");
        (await Invoke<Task<bool>>(operation, "SaveChangesAsync", CancellationToken.None)).Should().BeTrue();
        ((CqrsCacheProxy)cache).Invalidations.Should().Be(1);
        (await Invoke<Task<IReadOnlyList<string>>>(read, "NamesAsync", CancellationToken.None)).Should().Equal("Zoe");
        ((CqrsCacheProxy)cache).FactoryCalls.Should().Be(2);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public void EntityCapabilitiesSuppressPortsAndKeylessSourcesCannotAcquireWrites()
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseCqrsSelectors().GenerateOnlyConfiguredEntities());
        builder.Entity<ClosedRow>().WriteEnabled = false;
        builder.Entity<ClosedRow>().HasCommand("Ignored").Delete();
        builder.Entity<ClosedOther>().ReadEnabled = false;
        builder.Entity<ClosedView>().HasQuery("Views").List();
        var model = RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: builder.Build());
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        TypeOf(assembly, "IReadClosedRowRepository").Should().NotBeNull();
        TypeOf(assembly, "IWriteClosedRowRepository", required: false).Should().BeNull();
        TypeOf(assembly, "ClosedRowSelector").IsPublic.Should().BeFalse();
        model.Entities.Single(static e => e.EntityName == "ClosedRow").Commands.Should().BeEmpty();
        TypeOf(assembly, "IReadClosedOtherRepository", required: false).Should().BeNull();
        TypeOf(assembly, "IWriteClosedOtherRepository").Should().NotBeNull();
        TypeOf(assembly, "IReadClosedViewRepository").Should().NotBeNull();
        TypeOf(assembly, "IWriteClosedViewRepository", required: false).Should().BeNull();
    }

    [Fact]
    public async Task ScopedRegistrationSharesWithinOneRequestAndIsolatesPendingChangesBetweenRequests()
    {
        await using var context = Context();
        await SeedAsync(context);
        var assembly = ClosedRepositoryTests.Compile(Model(context), rejectWarnings: true);
        var services = new ServiceCollection();
        services.AddScoped(_ => new ClosedContext(new DbContextOptionsBuilder<ClosedContext>().UseSqlite(context.Database.GetDbConnection()).Options));
        assembly.GetType("RonSijm.RepoGen.ClosedContextSelectorRepositoryRegistration")!.GetMethod("AddClosedContextSelectorRepositories")!.Invoke(null, [services]);
        await using var provider = services.BuildServiceProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();
        var writeType = TypeOf(assembly, "IWriteClosedRowRepository");
        var write1 = scope1.ServiceProvider.GetRequiredService(writeType);
        var write2 = scope2.ServiceProvider.GetRequiredService(writeType);
        write1.Should().NotBeSameAs(write2).And.BeSameAs(scope1.ServiceProvider.GetRequiredService(writeType));
        var context1 = scope1.ServiceProvider.GetRequiredService<ClosedContext>();
        var context2 = scope2.ServiceProvider.GetRequiredService<ClosedContext>();
        context1.Should().NotBeSameAs(context2);
        var read2 = scope2.ServiceProvider.GetRequiredService(TypeOf(assembly, "IReadClosedRowRepository"));
        await Invoke<Task>(write1, "AddAsync", new ClosedRow { Id = 3, Name = "Pending" }, CancellationToken.None);
        (await Invoke<Task<int>>(write2, "SaveChangesAsync", CancellationToken.None)).Should().Be(0);
        (await Invoke<Task<int>>(Invoke<object>(read2, "GetAll"), "CountAsync", CancellationToken.None)).Should().Be(2);
        context2.ChangeTracker.Entries().Should().BeEmpty();
        (await Invoke<Task<int>>(write1, "SaveChangesAsync", CancellationToken.None)).Should().Be(1);
        (await Invoke<Task<int>>(Invoke<object>(read2, "GetAll"), "CountAsync", CancellationToken.None)).Should().Be(3);
        context1.DisposeCount.Should().Be(0);
        context2.DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task VisibilityPoliciesProtectReadSelectorsNamedRootsAndWrites()
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, configure: entity =>
        {
            entity.HasVisibilityPolicy("Visible", row => row.Id == 1);
            entity.HasQuery("All").BypassVisibility("Visible").List();
            entity.HasQuery("FromVisible").From(sources => sources.Set<ClosedRow>()).List();
            entity.HasCommand("DeleteVisible").Where<int>((row, id) => row.Id == id).Delete();
            entity.HasCommand("DeleteAll").BypassVisibility("Visible").Where<int>((row, id) => row.Id == id).Delete();
        });
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        await using var provider = Provider(assembly, context);
        var read = provider.GetRequiredService(TypeOf(assembly, "IReadClosedRowRepository"));
        var write = provider.GetRequiredService(TypeOf(assembly, "IWriteClosedRowRepository"));
        (await Invoke<Task<ClosedRow?>>(Invoke<object>(read, "ById", 2), "AsEntityAsync", CancellationToken.None)).Should().BeNull();
        (await Invoke<Task<int>>(Invoke<object>(read, "GetAllUnordered"), "CountAsync", CancellationToken.None)).Should().Be(1);
        (await Invoke<Task<IReadOnlyList<ClosedRow>>>(read, "AllAsync", CancellationToken.None)).Should().HaveCount(2);
        (await Invoke<Task<IReadOnlyList<ClosedRow>>>(read, "FromVisibleAsync", CancellationToken.None)).Should().ContainSingle();
        read.GetType().GetMethod("GetAllForQuery").Should().BeNull();
        TypeOf(assembly, "IWriteClosedRowRepository").GetMethod("GetAllForQuery").Should().BeNull();
        (await Invoke<Task<int>>(write, "DeleteVisibleAsync", 2, CancellationToken.None)).Should().Be(0);
        (await Invoke<Task<int>>(write, "DeleteAllAsync", 2, CancellationToken.None)).Should().Be(1);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task AccessorSourceArgumentDoesNotShadowTheReadBackingRepository()
    {
        await using var context = Context();
        await SeedAsync(context);
        var assembly = ClosedRepositoryTests.Compile(Model(context, configure: entity =>
            entity.HasFilter<string>("BySource", (row, source) => row.Name == source)), rejectWarnings: true);
        await using var provider = Provider(assembly, context);
        var read = provider.GetRequiredService(TypeOf(assembly, "IReadClosedRowRepository"));
        (await Invoke<Task<IReadOnlyList<ClosedRow>>>(Invoke<object>(read, "BySource", "Ada"), "AsEntitiesAsync", CancellationToken.None))
            .Should().ContainSingle().Which.Name.Should().Be("Ada");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public void DisabledOperationsAreIgnoredButActiveKeylessWritesAreDiagnosed()
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseCqrsSelectors().GenerateOnlyConfiguredEntities());
        builder.Entity<ClosedRow>().ReadEnabled = false;
        builder.Entity<ClosedRow>().HasQuery("Unused").ProjectTo("Missing").List();
        RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, true, builder.Build()).Entities.Single().Queries.Should().BeEmpty();
        builder.Entity<ClosedView>().HasCommand("Invalid").Delete();
        Action keyless = () => RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, true, builder.Build());
        keyless.Should().Throw<InvalidOperationException>().WithMessage("RGD1604:*");
        builder.Entity<ClosedView>().WriteEnabled = false;
        ClosedRepositoryTests.Compile(RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, true, builder.Build()), rejectWarnings: true);
    }

    [Fact]
    public void OptionRemainsDisabledByDefaultAndCanBeExplicitlyDisabled()
    {
        using var context = Context();
        var simple = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()).Build();
        var disabled = new RepositoryModelBuilder().Configure(options => options.UseCqrsSelectors().UseSimpleDefaults().UseCqrsSelectors(false)).Build();
        simple.Options.CqrsSelectors.Should().BeFalse();
        disabled.Options.CqrsSelectors.Should().BeFalse();
        RepositorySourceRenderer.Render(RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: disabled))
            .Should().Be(RepositorySourceRenderer.Render(RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: simple)));
        new RepositoryModelBuilder().Configure(options => options.UseCqrsSelectors().UseCompatibilityDefaults()).Build().Options.CqrsSelectors.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompatibleSurfacesAndOwnedSelectorLifetimesFailBeforeGeneration(bool owned)
    {
        using var context = Context();
        Action generate = () => Model(context, options =>
        {
            if (owned)
            {
                options.UseAsyncContextLeases();
            }
            else
            {
                options.UseClosedRepositories("Other.Contracts");
            }
        });
        generate.Should().Throw<InvalidOperationException>().WithMessage(owned ? "RGD1201:*" : "RGD1601:*");
    }

    [Fact]
    public void ExistingSelectorsMustBeRegeneratedAndMemberConflictsFailAtDesignTime()
    {
        using var context = Context();
        var model = Model(context);
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseCqrsSelectors().GenerateOnlyConfiguredEntities());
        builder.Entity<ClosedRow>();
        Action reuse = () => RepositoryModelFactory.Create(context, assembly, design: builder.Build());
        reuse.Should().Throw<InvalidOperationException>().WithMessage("RGD1602:*");
        Action collision = () => Model(context, configure: entity => entity.HasProjection<string>("AsEntityAsync", row => row.Name));
        collision.Should().Throw<InvalidOperationException>().WithMessage("RGD1603:*");
        Action constructor = () => Model(context, options => options.GenerateTrackingMethods().UseTrackingMethodNames("ReadClosedRowSelector"));
        constructor.Should().Throw<InvalidOperationException>().WithMessage("*Tracking method name*");
        Action field = () => Model(context, configure: entity => entity.HasProjection<string>("inner", row => row.Name));
        field.Should().Throw<InvalidOperationException>().WithMessage("RGD1603:*");
        Action trackingField = () => Model(context, options => options.GenerateTrackingMethods().UseTrackingMethodNames("inner"));
        trackingField.Should().Throw<InvalidOperationException>().WithMessage("*Tracking method name*");
    }

    [Fact]
    public void TypeCollisionsBetweenReadWrappersAndOtherEntitiesFailBeforeRendering()
    {
        using var context = new CqrsCollisionContext(new DbContextOptionsBuilder<CqrsCollisionContext>().UseInMemoryDatabase("cqrs-collision").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().UseCqrsSelectors());
        Action create = () => RepositoryModelFactory.Create(context, typeof(CqrsCollisionContext).Assembly, true, builder.Build());
        create.Should().Throw<InvalidOperationException>().WithMessage("RGD1605:*");
    }

    [Fact]
    public void InternalOutputPortsRetainTheirInternalNamedQueries()
    {
        using var context = Context();
        var model = Model(context, configure: entity => entity.HasQuery("Rows").ProjectTo(row => new ClosedRowDto(row.Id, row.Name)).List());
        var entity = model.Entities.Single();
        var assembly = ClosedRepositoryTests.Compile(model with
        {
            Entities =
            [
                entity with
                {
                    Accessibility = "internal",
                    Queries = entity.Queries.Select(static query => query with { Accessibility = "internal" }).ToArray()
                }
            ]
        }, rejectWarnings: true);
        var port = TypeOf(assembly, "IReadClosedRowRepository");
        port.IsPublic.Should().BeFalse();
        port.GetMethod("RowsAsync").Should().NotBeNull();
    }

    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    public void BothSelectorOutputLayoutsCompileAndAreDeterministic(RepositoryOutputLayout layout)
    {
        using var context = Context();
        var model = Model(context, configure: entity => entity.HasQuery("Rows").List());
        var bundle = RepositoryGenerator.Generate(model, layout);
        bundle.Files.Should().BeEquivalentTo(RepositoryGenerator.Generate(model, layout).Files, options => options.WithStrictOrdering());
        var registration = bundle.Files.Where(static file => file.Kind == RepositoryOutputKind.Repositories)
            .Count(static file => file.Contents.Contains("static class ClosedContextSelectorRepositoryRegistration", StringComparison.Ordinal));
        registration.Should().Be(1);
        var references = References();
        var compilation = CSharpCompilation.Create("Split", bundle.Files.Where(static f => f.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Projections)
            .Select(static f => CSharpSyntaxTree.ParseText(f.Contents)), references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error || d.Severity == DiagnosticSeverity.Warning && d.Id != "CS1701").Should().BeEmpty();
    }

    private static RepositoryGenerationModel Model(ClosedContext context, Action<RepositoryGenerationOptionsBuilder>? options = null,
        Action<RepositoryEntityBuilder<ClosedRow>>? configure = null)
    {
        var builder = new RepositoryModelBuilder().Configure(o => o.UseSimpleDefaults().UseCqrsSelectors().GenerateOnlyConfiguredEntities());
        if (options is not null)
        {
            builder.Configure(options);
        }

        var entity = builder.Entity<ClosedRow>();
        entity.HasDefaultSort(row => row.Name);
        entity.HasSortableFields(row => row.Id, row => row.Name);
        configure?.Invoke(entity);
        return RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, generateSelectors: true, design: builder.Build());
    }

    private static ClosedContext Context() => new(new DbContextOptionsBuilder<ClosedContext>().UseSqlite("Data Source=:memory:").Options);

    private static async Task SeedAsync(ClosedContext context)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.Rows.AddRange(new ClosedRow { Id = 1, Name = "Ada" }, new ClosedRow { Id = 2, Name = "Zoe" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static Type TypeOf(Assembly assembly, string name, bool required = true) => assembly.GetType(typeof(ClosedRow).Namespace + "." + name, throwOnError: required)!;

    private static T Invoke<T>(object target, string name, params object[] args) =>
        (T)target.GetType().GetMethods().Single(method => method.Name == name && !method.IsGenericMethod && method.GetParameters().Length == args.Length).Invoke(target, args)!;

    private static object Generic(object target, string name, Type[] types, params object[] args) => target.GetType().GetMethods()
        .Single(method => method.Name == name && method.IsGenericMethod && method.GetGenericArguments().Length == types.Length)
        .MakeGenericMethod(types).Invoke(target, args)!;

    private static object Property(object target, string name) => target.GetType().GetProperty(name)!.GetValue(target)!;

    private static async Task<object> ResultAsync(object task)
    {
        await (Task)task;
        return Property(task, "Result");
    }

    private static ServiceProvider Provider(Assembly assembly, ClosedContext context, bool resolver = false, object? cache = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        if (resolver)
        {
            var resolverType = assembly.GetType("RonSijm.RepoGen.ScopedDbContextResolver`1")!.MakeGenericType(typeof(ClosedContext));
            services.AddSingleton(assembly.GetType("RonSijm.RepoGen.IDbContextResolver`1")!.MakeGenericType(typeof(ClosedContext)), Activator.CreateInstance(resolverType, context)!);
        }

        if (cache is not null)
        {
            services.AddSingleton(assembly.GetType("RonSijm.RepoGen.IRepositoryCacheProvider")!, cache);
        }

        var register = assembly.GetType("RonSijm.RepoGen.ClosedContextSelectorRepositoryRegistration")!.GetMethod("AddClosedContextSelectorRepositories")!;
        register.Invoke(null, register.GetParameters().Length == 2 ? [services, cache is not null] : [services]);
        return services.BuildServiceProvider();
    }

    private static Diagnostic[] CompileErrors(RepositoryGenerationModel model, string consumer)
    {
        var sources = new[] { RepositorySourceRenderer.Render(model), ProjectionSourceRenderer.Render(model) };
        var references = References();
        var generated = CSharpCompilation.Create("CqrsGenerated", sources.Select(static source => CSharpSyntaxTree.ParseText(source)),
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        generated.Emit(stream).Success.Should().BeTrue();
        return CSharpCompilation.Create("ReadConsumer", [CSharpSyntaxTree.ParseText(consumer)],
                references.Append(MetadataReference.CreateFromImage(stream.ToArray())), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
    }

    private static MetadataReference[] References() => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Append(typeof(CqrsSelectorTests).Assembly.Location).Append(typeof(DbContext).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(static path => MetadataReference.CreateFromFile(path)).ToArray();
}

public sealed class CqrsCollisionContext(DbContextOptions<CqrsCollisionContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CqrsCollisionRow>();
        modelBuilder.Entity<ReadCqrsCollisionRow>();
    }
}

public sealed class CqrsCollisionRow
{
    public int Id { get; set; }
}

public sealed class ReadCqrsCollisionRow
{
    public int Id { get; set; }
}

public class CqrsCacheProxy : DispatchProxy
{
    private readonly Dictionary<string, object> entries = [];
    public int FactoryCalls { get; private set; }
    public int Invalidations { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name == "InvalidateTagAsync")
        {
            Invalidations++;
            entries.Clear();
            return Task.CompletedTask;
        }

        var key = (string)args![0]!;
        if (entries.TryGetValue(key, out var cached))
        {
            return cached;
        }

        FactoryCalls++;
        var factory = args.OfType<Delegate>().Single();
        var result = factory.DynamicInvoke(args[^1])!;
        entries.Add(key, result);
        return result;
    }
}