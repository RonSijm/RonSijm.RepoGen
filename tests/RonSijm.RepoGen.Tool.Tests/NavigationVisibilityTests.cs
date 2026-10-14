using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RonSijm.RepoGen.Caching;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NavigationVisibilityTests
{
    [Theory]
    [InlineData("predicate")]
    [InlineData("conditional")]
    [InlineData("sort")]
    [InlineData("projection")]
    [InlineData("group")]
    [InlineData("scalar")]
    [InlineData("dictionary")]
    [InlineData("cursor")]
    [InlineData("membership")]
    [InlineData("stage")]
    [InlineData("source")]
    [InlineData("efProperty")]
    public void EveryInspectableReadClauseRequiresReview(string clause)
    {
        using var context = Context();
        var builder = Builder();
        ConfigureClause(builder.Entity<LoadingOrder>().HasQuery("Inspect"), clause);
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2001:*Customer*CustomerLive*");
    }

    [Theory]
    [InlineData("selectors")]
    [InlineData("cqrs")]
    [InlineData("closed")]
    public void ReviewedConfigurationCompilesAcrossAllSurfacesWithoutRuntimeReviewApis(string surface)
    {
        using var context = Context();
        var builder = Builder(surface);
        builder.Entity<LoadingOrder>().ReviewNavigationVisibility<LoadingCustomer>("CustomerLive", "The explicit ID predicate excludes the protected rows.");
        builder.Entity<LoadingOrder>().HasQuery("Inspect").Where(order => order.Customer != null && order.Customer.Id == 1).Select(order => order.Id).List();
        var model = Model(context, builder);
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        assembly.GetTypes().Where(static type => type.IsPublic).SelectMany(static type => type.GetMethods())
            .Should().NotContain(static method => method.Name.Contains("NavigationVisibility", StringComparison.Ordinal));
        model.NavigationVisibility!.SelectMany(static operation => operation.Accesses).Should().ContainSingle()
            .Which.ReviewScope.Should().Be("entity");
        using var manifest = JsonDocument.Parse(RepositoryManifestRenderer.Render(model));
        manifest.RootElement.GetProperty("navigationVisibility").GetProperty("requiredByDefault").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void DisabledDefaultsAreByteCompatibleAndReviewDoesNotRewriteQueryCode()
    {
        using var context = Context();
        var builder = Builder(required: false);
        builder.Entity<LoadingOrder>().HasQuery("Inspect").Select(order => order.Customer!.Name).List();
        var original = Model(context, builder);
        original.NavigationVisibility.Should().BeNull();
        RepositoryManifestRenderer.Render(original).Should().NotContain("navigationVisibility");
        builder.Configure(options => options.RequireNavigationVisibilityReview());
        builder.Entity<LoadingOrder>().ReviewNavigationVisibility<LoadingCustomer>("CustomerLive", "Historical names are deliberately visible.");
        var reviewed = Model(context, builder);
        RepositorySourceRenderer.Render(reviewed).Should().Be(RepositorySourceRenderer.Render(original));
        ClosedRepositorySourceRenderer.RenderContracts(reviewed).Should().Be(ClosedRepositorySourceRenderer.RenderContracts(original));
    }

    [Fact]
    public void PerScopeOverridesInheritAndCanBeResetAndLocalReasonsTakePrecedence()
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<LoadingOrder>();
        entity.RequireNavigationVisibilityReview(false);
        var query = entity.HasQuery("Inspect").Select(order => order.Customer!.Id).List();
        Model(context, builder).NavigationVisibility!.Single().Required.Should().BeFalse();
        query.RequireNavigationVisibilityReview();
        Action unreviewed = () => Model(context, builder);
        unreviewed.Should().Throw<InvalidOperationException>().WithMessage("RGD2001:*");
        entity.ReviewNavigationVisibility<LoadingCustomer>("CustomerLive", "Entity default");
        query.ReviewNavigationVisibility<LoadingCustomer>("CustomerLive", "Operation-specific historical lookup");
        var access = Model(context, builder).NavigationVisibility!.Single().Accesses.Single();
        access.ReviewReason.Should().Be("Operation-specific historical lookup");
        access.ReviewScope.Should().Be("query");
        query.RequireNavigationVisibilityReview(null);
        entity.RequireNavigationVisibilityReview(null);
        Model(context, builder).NavigationVisibility!.Single().Required.Should().BeTrue();
    }

    [Fact]
    public void ProjectionReviewIsReusableAndAnUnusedProjectionOrLoadProfileIsNotAnOperation()
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<LoadingOrder>();
        entity.HasLoadProfile("Unused", load => load.Include(order => order.Notes));
        entity.HasProjection(order => order.Customer == null ? string.Empty : order.Customer.Name).HasMethodName("CustomerName")
            .ReviewNavigationVisibility<LoadingCustomer>("CustomerLive", "Approved historical name projection");
        entity.HasProjection(order => order.Notes.Count).HasMethodName("Unused");
        entity.HasQuery("Name").ProjectTo("CustomerName").List();
        var model = Model(context, builder);
        model.NavigationVisibility!.Should().ContainSingle().Which.Accesses.Single().ReviewScope.Should().Be("projection");
        ClosedRepositoryTests.Compile(model, rejectWarnings: true);
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("nested")]
    [InlineData("collection")]
    [InlineData("owned")]
    [InlineData("skip")]
    public void LoadingProfilesIncludeAllMappedNavigationKinds(string kind)
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<LoadingOrder>();
        entity.HasLoadProfile("Detail", load =>
        {
            switch (kind)
            {
                case "reference": load.Include(order => order.Customer); break;
                case "nested": load.Include(order => order.Lines).ThenInclude(line => line.Product); break;
                case "collection": load.Include(order => order.Notes); break;
                case "owned": load.Include(order => order.Info); break;
                case "skip": load.Include(order => order.Labels); break;
            }
        });
        entity.HasQuery("Detail").UseLoadProfile("Detail").ProjectTo(order => LoadingMapper.Details(order)).List();
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2001:*");
    }

    [Fact]
    public void AllPoliciesNeedIndividualReceiptsEvenForExcludedRepositoriesAndRootBypasses()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<LoadingCustomer>().HasVisibilityPolicy("Second", customer => customer.Id > 0);
        var query = builder.Entity<LoadingOrder>().HasQuery("Inspect")
            .ReviewNavigationVisibility<LoadingCustomer>("CustomerLive", "Reviewed one policy")
            .From(sources => sources.Set<LoadingCustomer>().Where(customer => customer.Id > 0)
                .SelectMany(customer => sources.Set<LoadingOrder>().Where(order => order.Customer!.Id == customer.Id), (customer, order) => order.Id)).List();
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2001:*Second*");
        query.ReviewNavigationVisibility<LoadingCustomer>("Second", "Reviewed second policy");
        Model(context, builder).NavigationVisibility!.Single().Accesses.Should().HaveCount(2);
        var root = builder.Entity<LoadingOrder>().HasQuery("Bypass").BypassVisibility("CustomerLive", "Second");
        root.From(sources => sources.Set<LoadingCustomer>().Join(sources.Set<LoadingOrder>(), customer => customer.Id, order => order.CustomerId,
            (customer, order) => order.Customer!.Id)).List();
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2001:*");
    }

    [Fact]
    public void DirectSetsAndJoinsDoNotNeedNavigationReceipts()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<LoadingOrder>().HasQuery("Join").From(sources => sources.Set<LoadingOrder>()
            .Join(sources.Set<LoadingCustomer>(), order => order.CustomerId, customer => customer.Id, (order, customer) => customer.Name)).List();
        var model = Model(context, builder);
        model.NavigationVisibility!.Single().Accesses.Should().BeEmpty();
        ClosedRepositoryTests.Compile(model, rejectWarnings: true);
    }

    [Fact]
    public void NavigationReferencesInsideSourceVisibilityPoliciesAreReviewed()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<LoadingOrder>().HasVisibilityPolicy("Owner", order => order.Customer!.Id > 0);
        builder.Entity<LoadingOrder>().HasQuery("Join").From(sources => sources.Set<LoadingOrder>().Select(order => order.Id)).List();
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2001:*CustomerLive*");
    }

    [Theory]
    [InlineData("predicate")]
    [InlineData("result")]
    public void TrackedCommandSelectionAndSavedResultNavigationAreReviewed(string clause)
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<LoadingOrder>();
        entity.WriteEnabled = true;
        var command = entity.HasCommand("Remove").Where(order => order.Id == 1).TrackedDelete().Save();
        if (clause == "predicate")
        {
            command.Where(order => order.Customer != null);
        }
        else
        {
            command.Returns(order => order.Customer!.Id);
        }

        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2001:*");
        command.ReviewNavigationVisibility<LoadingCustomer>("CustomerLive", "The service supplies an authorized root identity");
        Model(context, builder).NavigationVisibility!.Single().Kind.Should().Be("command");
    }

    [Theory]
    [InlineData("missingPolicy")]
    [InlineData("wrongCase")]
    [InlineData("unmappedTarget")]
    public void ExplicitReviewsAreValidatedEvenWhenTheGuardIsDisabled(string invalid)
    {
        using var context = Context();
        var builder = Builder(required: false);
        var entity = builder.Entity<LoadingOrder>();
        if (invalid == "unmappedTarget")
        {
            entity.ReviewNavigationVisibility<CommandRow>("CustomerLive", "Invalid");
        }
        else
        {
            entity.ReviewNavigationVisibility<LoadingCustomer>(invalid == "wrongCase" ? "customerlive" : "Missing", "Invalid");
        }

        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2002:*");
    }

    [Fact]
    public void ReceiptsRequireReasonsAndDoNotAcceptConflictingDuplicateModuleDeclarations()
    {
        var builder = Builder();
        var entity = builder.Entity<LoadingOrder>();
        Action emptyPolicy = () => entity.ReviewNavigationVisibility<LoadingCustomer>(" ", "Reason");
        emptyPolicy.Should().Throw<ArgumentException>();
        Action emptyReason = () => entity.ReviewNavigationVisibility<LoadingCustomer>("CustomerLive", " ");
        emptyReason.Should().Throw<ArgumentException>();
        entity.ReviewNavigationVisibility<LoadingCustomer>(" CustomerLive ", " Reason ");
        Action duplicate = () => entity.ReviewNavigationVisibility<LoadingCustomer>("CustomerLive", "Different reason");
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("RGD2003:*");
        builder.Build().Entities.Single(static entity => entity.EntityType == typeof(LoadingOrder)).NavigationVisibilityReviews.Single().Reason.Should().Be("Reason");
    }

    [Fact]
    public void ResultBuilderReviewRejectsStaleBuildersAndDisabledCapabilitiesAreNotAudited()
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<LoadingOrder>();
        var stale = entity.HasQuery("Unused").Results<LoadingOrder>();
        stale.Select(order => order.Customer!.Name).List();
        Action review = () => stale.ReviewNavigationVisibility<LoadingCustomer>("CustomerLive", "Reason");
        review.Should().Throw<InvalidOperationException>();
        entity.ReadEnabled = false;
        entity.WriteEnabled = true;
        entity.HasCommand("Remove").Where(order => order.Id == 1).Delete();
        Model(context, builder).NavigationVisibility!.Should().ContainSingle().Which.Kind.Should().Be("command");
    }

    [Fact]
    public void CqrsEntitiesWithBothCapabilitiesDisabledDoNotRequireUnusedProjectionReviews()
    {
        using var context = Context();
        var builder = Builder("cqrs");
        builder.Entity<LoadingOrder>(entity =>
        {
            entity.ReadEnabled = false;
            entity.WriteEnabled = false;
            entity.HasProjection(order => order.Customer == null ? string.Empty : order.Customer.Name);
        });
        Model(context, builder).NavigationVisibility.Should().BeEmpty();
    }

    [Fact]
    public void AuditsKeepOverloadIdentityAndEachOperationsReasonDeterministically()
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<LoadingOrder>();
        entity.HasQuery("Find").ReviewNavigationVisibility<LoadingCustomer>("CustomerLive", "ID lookup")
            .Where<int>((order, value) => order.Id == value && order.Customer != null).Select(order => order.Id).List();
        entity.HasQuery("Find").ReviewNavigationVisibility<LoadingCustomer>("CustomerLive", "Name lookup")
            .Where<string>((order, value) => order.Name == value && order.Customer != null).Select(order => order.Id).List();
        var model = Model(context, builder);
        var operations = model.NavigationVisibility!;
        operations.Should().HaveCount(2);
        operations.Select(operation => operation.Operation).Distinct().Should().HaveCount(2);
        operations.Single(operation => operation.Operation.Contains("System.Int32", StringComparison.Ordinal)).Accesses.Single().ReviewReason.Should().Be("ID lookup");
        operations.Single(operation => operation.Operation.Contains("System.String", StringComparison.Ordinal)).Accesses.Single().ReviewReason.Should().Be("Name lookup");
        RepositoryManifestRenderer.Render(Model(context, builder)).Should().Be(RepositoryManifestRenderer.Render(model));
        ClosedRepositoryTests.Compile(model, rejectWarnings: true);
    }

    [Theory]
    [InlineData("predicate")]
    [InlineData("sort")]
    [InlineData("scalar")]
    [InlineData("dictionary")]
    [InlineData("cursor")]
    [InlineData("efProperty")]
    public void NavigationCacheDependenciesIncludeClausesWithoutEntityProjection(string clause)
    {
        using var context = Context();
        var builder = Builder(required: false);
        ConfigureClause(builder.Entity<LoadingOrder>().HasQuery("Inspect"), clause);
        Model(context, builder).Entities.Single().Queries.Single().Cache.DependentEntityTypeNames
            .Should().Contain(RepositoryModelFactory.CSharpTypeName(typeof(LoadingCustomer)));
    }

    [Fact]
    public async Task ReviewIsNotAnImplicitFilterAndExplicitNavigationPredicatesRemainServerSide()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Owners.AddRange(new CommandOwner { Id = 1 }, new CommandOwner { Id = 2 });
        context.Rows.AddRange(new CommandRow { Name = "visible", OwnerId = 1 }, new CommandRow { Name = "historic", OwnerId = 2 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Review.Contracts")
            .GenerateOnlyConfiguredEntities().RequireNavigationVisibilityReview());
        builder.Entity<CommandOwner>(entity =>
        {
            entity.RepositoryEnabled = false;
            entity.HasVisibilityPolicy("Live", owner => owner.Id == 1);
        });
        builder.Entity<CommandRow>(entity =>
        {
            entity.ReviewNavigationVisibility<CommandOwner>("Live", "Historical read deliberately includes excluded owners");
            entity.HasQuery("Historical").OrderBy(row => row.Id).Select(row => row.Owner == null ? -1 : row.Owner.Id).List();
            entity.HasQuery("Current").Where(row => row.Owner != null && row.Owner.Id == 1).OrderBy(row => row.Id).Select(row => row.Owner == null ? -1 : row.Owner.Id).List();
        });
        var assembly = ClosedRepositoryTests.Compile(RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build()), rejectWarnings: true);
        var operations = Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, context)!;
        (await Call<Task<IReadOnlyList<int>>>(operations, "HistoricalAsync", CancellationToken.None)).Should().Equal(1, 2);
        (await Call<Task<IReadOnlyList<int>>>(operations, "CurrentAsync", CancellationToken.None)).Should().Equal(1);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task RelatedWritesInvalidateCachedRootOnlyResultsWhosePredicateUsesANavigation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new LoadingContext(new DbContextOptionsBuilder<LoadingContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Orders.Add(new LoadingOrder { Customer = new LoadingCustomer { Name = "Ada", Address = new LoadingAddress { City = "Amsterdam" } } });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = Builder(required: false);
        builder.Configure(options => options.UseClosedCaching("review-fixture"));
        builder.Entity<LoadingCustomer>(entity =>
        {
            entity.RepositoryEnabled = true;
            entity.ReadEnabled = false;
            entity.HasCommand("Rename").Update(update => update.SetProperty(customer => customer.Name, (LoadingCustomer customer, string name) => name));
        });
        builder.Entity<LoadingOrder>().HasQuery("Ids").Cache().Where(order => order.Customer != null && order.Customer.Name == "Ada").Select(order => order.Id).List();
        var assembly = ClosedRepositoryTests.Compile(Model(context, builder), rejectWarnings: true);
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryRepositoryResultCache(memory);
        var orders = Activator.CreateInstance(assembly.GetType(typeof(LoadingOrder).Namespace + ".LoadingOrderRepositoryOperations")!, context, cache)!;
        var customers = Activator.CreateInstance(assembly.GetType(typeof(LoadingCustomer).Namespace + ".LoadingCustomerRepositoryOperations")!, context, cache)!;
        (await Call<Task<IReadOnlyList<int>>>(orders, "IdsAsync", CancellationToken.None)).Should().ContainSingle();
        (await Call<Task<IReadOnlyList<int>>>(orders, "IdsAsync", CancellationToken.None)).Should().ContainSingle();
        (await Call<Task<int>>(customers, "RenameAsync", "Grace", CancellationToken.None)).Should().Be(1);
        (await Call<Task<IReadOnlyList<int>>>(orders, "IdsAsync", CancellationToken.None)).Should().BeEmpty();
    }

    internal static RepositoryModelBuilder Builder(string surface = "closed", bool required = true)
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options =>
        {
            options.UseSimpleDefaults().GenerateOnlyConfiguredEntities().RequireNavigationVisibilityReview(required);
            if (surface == "closed")
            {
                options.UseClosedRepositories("Review.Contracts");
            }
            else if (surface == "cqrs")
            {
                options.UseCqrsSelectors();
            }
        });
        builder.Entity<LoadingOrder>(entity =>
        {
            if (surface != "selectors")
            {
                entity.WriteEnabled = false;
            }
        });
        builder.Entity<LoadingCustomer>(entity =>
        {
            entity.RepositoryEnabled = false;
            entity.HasVisibilityPolicy("CustomerLive", customer => customer.Name != "hidden");
        });
        builder.Entity<LoadingProduct>(entity =>
        {
            entity.RepositoryEnabled = false;
            entity.HasVisibilityPolicy("ProductLive", product => product.Name != "hidden");
        });
        builder.Entity<LoadingNote>(entity =>
        {
            entity.RepositoryEnabled = false;
            entity.HasVisibilityPolicy("Public", note => note.IsPublic);
        });
        builder.Entity<LoadingInfo>(entity =>
        {
            entity.RepositoryEnabled = false;
            entity.HasVisibilityPolicy("Metadata", info => info.Text != "private");
        });
        builder.Entity<LoadingLabel>(entity =>
        {
            entity.RepositoryEnabled = false;
            entity.HasVisibilityPolicy("LabelLive", label => label.Name != "hidden");
        });
        return builder;
    }

    private static void ConfigureClause(RepositoryQueryBuilder<LoadingOrder> query, string clause)
    {
        switch (clause)
        {
            case "predicate": query.Where(order => order.Customer != null).Select(order => order.Id).List(); break;
            case "conditional": query.WhereIf<bool>(enabled => enabled, (order, enabled) => order.Customer != null).Select(order => order.Id).List(); break;
            case "sort": query.OrderBy(order => order.Customer!.Name).Select(order => order.Id).List(); break;
            case "projection": query.Select(order => order.Customer!.Name).List(); break;
            case "group": query.GroupBy(order => order.Customer!.Name, group => group.Count()); break;
            case "scalar": query.Results<LoadingOrder>().Max(order => order.Customer!.Id); break;
            case "dictionary": query.Results<LoadingOrder>().Dictionary(order => order.Id, order => order.Customer!.Name); break;
            case "cursor":
                query.OrderBy(order => order.Id).ProjectTo(order => order.Id)
                    .CursorPaged<int>((order, cursor) => order.Customer!.Id > cursor, order => order.Id); break;
            case "membership": query.WhereIn(order => order.Customer!.Id, (int[] ids) => ids).Select(order => order.Id).List(); break;
            case "stage": query.Results<LoadingOrder>().Where(order => order.Customer != null).Select(order => order.Id).List(); break;
            case "source": query.From(sources => sources.Set<LoadingOrder>().Select(order => order.Customer!.Name)).List(); break;
            case "efProperty": query.Select(order => EF.Property<LoadingCustomer>(order, "Customer").Name).List(); break;
            default: throw new ArgumentOutOfRangeException(nameof(clause));
        }
    }

    private static LoadingContext Context() => new(new DbContextOptionsBuilder<LoadingContext>().UseSqlite("Data Source=:memory:").Options);

    private static RepositoryGenerationModel Model(LoadingContext context, RepositoryModelBuilder builder) =>
        RepositoryModelFactory.Create(context, typeof(LoadingOrder).Assembly, true, builder.Build());

    private static T Call<T>(object instance, string name, params object[] arguments) => (T)instance.GetType().GetMethod(name)!.Invoke(instance, arguments)!;
}