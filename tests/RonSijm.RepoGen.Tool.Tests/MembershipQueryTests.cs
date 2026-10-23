using System.Collections;
using System.Data.Common;
using System.Reflection;
using System.Runtime.ExceptionServices;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Caching;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

#pragma warning disable CA1861 // Deliberately independent mutable input arrays exercise snapshots without cross-test sharing.

public sealed class MembershipQueryTests
{
    [Theory]
    [InlineData("closed")]
    [InlineData("cqrs")]
    [InlineData("selectors")]
    public async Task MembershipRunsServerSideOnEverySurfaceWithoutChangingVisibility(string surface)
    {
        await using var fixture = await Fixture.CreateAsync(entity =>
        {
            entity.HasVisibilityPolicy("Visible", row => row.Visible);
            entity.HasQuery("Find").WhereIn(row => row.Id, (int[] ids) => ids).OrderBy(row => row.Id).Select(row => row.Id).List();
            entity.HasQuery("Exclude").WhereNotIn(row => row.Id, (int[] ids) => ids).OrderBy(row => row.Id).Select(row => row.Id).List();
        }, surface);
        (await fixture.Read("FindAsync", new[] { 1, 2, 4 })).Should().Equal(1, 2);
        (await fixture.Read("FindAsync", Array.Empty<int>())).Should().BeEmpty();
        (await fixture.Read("ExcludeAsync", new[] { 1 })).Should().Equal(2, 3);
        (await fixture.Read("ExcludeAsync", Array.Empty<int>())).Should().Equal(1, 2, 3);
        fixture.Sql.Commands.Should().HaveCount(4);
        fixture.Sql.Commands[0].Text.Should().Contain("json_each").And.Contain("WHERE");
        fixture.Sql.Commands[0].Values.Should().Contain("[1,2,4]");
        if (surface != "selectors")
        {
            fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullElementsRetainLinqMembershipSemanticsIncludingNegation(bool negated)
    {
        await using var fixture = await Fixture.CreateAsync(entity =>
        {
            entity.HasVisibilityPolicy("Visible", row => row.Visible);
            var query = entity.HasQuery("Find");
            if (negated)
            {
                query.WhereNotIn(row => row.Name, (string?[] names) => names);
            }
            else
            {
                query.WhereIn(row => row.Name, (string?[] names) => names);
            }

            query.OrderBy(row => row.Id).Select(row => row.Id).List();
        });
        (await fixture.Read("FindAsync", new object[] { new string?[] { null } })).Should().Equal(negated ? [1, 2] : [3]);
        (await fixture.Read("FindAsync", new object[] { new string?[] { "A" } })).Should().Equal(negated ? [2, 3] : [1]);
        (await fixture.Read("FindAsync", new object[] { new string?[] { "A", null } })).Should().Equal(negated ? [2] : [1, 3]);
    }

    [Theory]
    [InlineData(RepositoryMembershipInputBehavior.EmptySet, false)]
    [InlineData(RepositoryMembershipInputBehavior.IgnoreFilter, false)]
    [InlineData(RepositoryMembershipInputBehavior.Reject, false)]
    [InlineData(RepositoryMembershipInputBehavior.EmptySet, true)]
    [InlineData(RepositoryMembershipInputBehavior.IgnoreFilter, true)]
    [InlineData(RepositoryMembershipInputBehavior.Reject, true)]
    public async Task NullAndEmptyPoliciesAreIndependentAndPrecedeContextAcquisition(RepositoryMembershipInputBehavior behavior, bool nullInput)
    {
        await using var fixture = await Fixture.CreateAsync(entity =>
        {
            var query = entity.HasQuery("Find").WhereIn(row => row.Id, (int[] ids) => ids, options =>
            {
                if (nullInput)
                {
                    options.NullInput(behavior).EmptyInput(RepositoryMembershipInputBehavior.Reject);
                }
                else
                {
                    options.EmptyInput(behavior);
                }
            }).AllowNullInput<int[]>("ids");
            query.OrderBy(row => row.Id).Select(row => row.Id).List();
        }, leases: true);
        if (behavior == RepositoryMembershipInputBehavior.Reject)
        {
            Func<Task> invalid = async () => await fixture.Read("FindAsync", new object?[] { nullInput ? null : Array.Empty<int>() });
            await invalid.Should().ThrowAsync<ArgumentException>();
            fixture.Provider.Calls.Should().Be(0);
            fixture.Sql.Commands.Should().BeEmpty();
        }
        else
        {
            (await fixture.Read("FindAsync", new object?[] { nullInput ? null : Array.Empty<int>() }))
                .Should().Equal(behavior == RepositoryMembershipInputBehavior.IgnoreFilter ? [1, 2, 3, 4] : []);
            fixture.Provider.Calls.Should().Be(1);
            fixture.Provider.Contexts.Single().WasDisposed.Should().BeTrue();
        }
    }

    [Fact]
    public async Task SnapshotDeduplicatesWithoutMutatingInputAndMaximumCountsRawValues()
    {
        RepositoryMembershipOptionsBuilder? retained = null;
        await using var fixture = await Fixture.CreateAsync(entity => entity.HasQuery("Find")
            .WhereIn(row => row.Id, (int[] ids) => ids, options =>
            {
                retained = options;
                options.Distinct().MaximumCount(3);
            })
            .OrderBy(row => row.Id).Select(row => row.Id).List(), leases: true);
        retained!.Distinct(false).MaximumCount(null); // A retained configuration builder cannot change its registration.
        var ids = new[] { 2, 1, 2 };
        (await fixture.Read("FindAsync", ids)).Should().Equal(1, 2);
        ids.Should().Equal(2, 1, 2);
        fixture.Sql.Commands.Single().Values.Should().Contain("[2,1]");
        Func<Task> overLimit = async () => await fixture.Read("FindAsync", new[] { 1, 1, 1, 1 });
        (await overLimit.Should().ThrowAsync<ArgumentOutOfRangeException>().WithMessage("*maximum raw*")).Which.ParamName.Should().Be("ids");
        fixture.Provider.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(RepositoryMembershipNullValueBehavior.Include)]
    [InlineData(RepositoryMembershipNullValueBehavior.Ignore)]
    [InlineData(RepositoryMembershipNullValueBehavior.Reject)]
    public async Task NullElementPolicyAndPostNormalizationEmptyPolicyAreExplicit(RepositoryMembershipNullValueBehavior behavior)
    {
        await using var fixture = await Fixture.CreateAsync(entity => entity.HasQuery("Find")
            .WhereIn(row => row.Name, (string?[] names) => names, options => options.NullValues(behavior).EmptyInput(RepositoryMembershipInputBehavior.Reject))
            .OrderBy(row => row.Id).Select(row => row.Id).List(), leases: true);
        if (behavior == RepositoryMembershipNullValueBehavior.Include)
        {
            (await fixture.Read("FindAsync", new object[] { new string?[] { null } })).Should().Equal(3);
        }
        else
        {
            Func<Task> invalid = async () => await fixture.Read("FindAsync", new object[] { new string?[] { null } });
            await invalid.Should().ThrowAsync<ArgumentException>();
            fixture.Provider.Calls.Should().Be(0);
        }
    }

    [Fact]
    public async Task NullsDiscardedByNormalizationStillCountTowardInputLimit()
    {
        await using var fixture = await Fixture.CreateAsync(entity => entity.HasQuery("Find")
            .WhereIn(row => row.Name, (string?[] names) => names, options => options.NullValues(RepositoryMembershipNullValueBehavior.Ignore).MaximumCount(1))
            .Select(row => row.Id).List(), leases: true);
        Func<Task> invalid = async () => await fixture.Read("FindAsync", new object[] { new string?[] { null, null } });
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
        fixture.Provider.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(RepositoryMembershipNullValueBehavior.Include)]
    [InlineData(RepositoryMembershipNullValueBehavior.Ignore)]
    public async Task NullableValueKeysAlsoHonorNullElementNormalization(RepositoryMembershipNullValueBehavior behavior)
    {
        await using var fixture = await Fixture.CreateAsync(entity => entity.HasQuery("Find")
            .WhereIn(row => row.Name == null ? (int?)null : row.Id, (int?[] ids) => ids,
                options => options.NullValues(behavior).Distinct()).OrderBy(row => row.Id).Select(row => row.Id).List());
        (await fixture.Read("FindAsync", new int?[] { null, 2, null })).Should().Equal(behavior == RepositoryMembershipNullValueBehavior.Include ? [2, 3] : [2]);
    }

    [Fact]
    public async Task ExplicitlyIgnoredMembershipStillAppliesOtherAndRequiredVisibilityPredicates()
    {
        await using var fixture = await Fixture.CreateAsync(entity =>
        {
            entity.HasVisibilityPolicy("Visible", row => row.Visible);
            entity.HasQuery("Find").Where(row => row.Id >= 2)
                .WhereIn(row => row.Id, (int[] ids) => ids, options => options.EmptyInput(RepositoryMembershipInputBehavior.IgnoreFilter))
                .OrderBy(row => row.Id).Select(row => row.Id).List();
        });
        (await fixture.Read("FindAsync", Array.Empty<int>())).Should().Equal(2, 3);
    }

    [Fact]
    public async Task DisablingCachingStillAllowsOneShotInputsOnACacheDeclaredOperation()
    {
        await using var fixture = await Fixture.CreateAsync(entity => entity.HasQuery("Find").Cache()
            .WhereIn(row => row.Id, (IEnumerable<int> ids) => ids).Select(row => row.Id).List());
        var ids = new MembershipOneShot([1]);
        (await fixture.Read("FindAsync", ids)).Should().Equal(1);
        ids.Enumerations.Should().Be(1);
    }

    [Fact]
    public void MembershipKeyNavigationsParticipateInDependencyInference()
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Membership.Dependencies").GenerateOnlyConfiguredEntities());
        builder.Entity<CommandRow>().HasQuery("Find")
            .WhereIn(row => row.Owner == null ? -1 : row.Owner.Id, (int[] ids) => ids).Select(row => row.Id).List();
        var model = RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build());
        model.Entities.Single().Queries.Single().Cache.DependentEntityTypeNames.Should().Contain("global::" + typeof(CommandOwner).FullName);
        ClosedRepositoryTests.Compile(model, rejectWarnings: true);
    }

    [Fact]
    public void AnonymousMembershipKeysRequireAnExplicitAlternativeInsteadOfUncompilableGeneratedTypes()
    {
        using var context = ModelContext();
        var builder = Builder("selectors");
        builder.Entity<MembershipRow>().HasQuery("Find")
            .WhereIn(row => new { row.Id }, (MembershipRequest request) => new[] { new { Id = request.Ids!.Length } }).Select(row => row.Id).List();
        Action generate = () => RepositoryModelFactory.Create(context, typeof(MembershipRow).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1901:*");
    }

    [Fact]
    public async Task OneShotInputsAreEnumeratedOnceAcrossCountedPagingAndDisposedBeforeLeaseAcquisition()
    {
        await using var fixture = await Fixture.CreateAsync(entity => entity.HasQuery("Find")
            .WhereIn(row => row.Id, (IEnumerable<int> ids) => ids, options => options.Distinct().MaximumCount(10))
            .OrderBy(row => row.Id).Select(row => row.Id).Paged(), leases: true);
        var ids = new MembershipOneShot([3, 1, 3]);
        var page = await fixture.Call<Task<PagedResult<int>>>("FindAsync", ids, 1, 1);
        page.TotalCount.Should().Be(2);
        page.Items.Should().Equal(1);
        ids.Enumerations.Should().Be(1);
        ids.Disposals.Should().Be(1);
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands.Should().OnlyContain(command => command.Text.Contains("json_each", StringComparison.Ordinal));
        fixture.Provider.Calls.Should().Be(1);
        fixture.Provider.Contexts.Single().WasDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task CancellationAndBoundedEnumerationDisposeInputsWithoutAcquiringAContext()
    {
        await using var fixture = await Fixture.CreateAsync(entity => entity.HasQuery("Find")
            .WhereIn(row => row.Id, (IEnumerable<int> ids) => ids, options => options.MaximumCount(2))
            .Select(row => row.Id).List(), leases: true);
        var ids = new MembershipOneShot([1, 1, 1, 1]);
        Func<Task> limit = async () => await fixture.Read("FindAsync", ids);
        await limit.Should().ThrowAsync<ArgumentOutOfRangeException>();
        ids.Yields.Should().Be(3);
        ids.Disposals.Should().Be(1);
        using var cancellation = new CancellationTokenSource();
        var canceled = new MembershipOneShot([1, 2], () => cancellation.Cancel());
        Func<Task> cancel = async () => await fixture.CallWithToken<Task<IReadOnlyList<int>>>("FindAsync", cancellation.Token, canceled);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        canceled.Disposals.Should().Be(1);
        fixture.Provider.Calls.Should().Be(0);
        var untouched = new MembershipOneShot([1]);
        Func<Task> preCanceled = async () => await fixture.CallWithToken<Task<IReadOnlyList<int>>>("FindAsync", cancellation.Token, untouched);
        await preCanceled.Should().ThrowAsync<OperationCanceledException>();
        untouched.Enumerations.Should().Be(0);
    }

    [Fact]
    public async Task StreamingSnapshotsOnceAndDisposesAnOwnedContextOnEarlyExit()
    {
        await using var fixture = await Fixture.CreateAsync(entity => entity.HasQuery("Find")
            .WhereIn(row => row.Id, (IEnumerable<int> ids) => ids).OrderBy(row => row.Id).Select(row => row.Id).Stream(), leases: true);
        var ids = new MembershipOneShot([1, 2, 3]);
        var stream = fixture.Call<IAsyncEnumerable<int>>("FindAsync", ids);
        fixture.Provider.Calls.Should().Be(0);
        ids.Enumerations.Should().Be(0);
        await using (var iterator = stream.GetAsyncEnumerator())
        {
            (await iterator.MoveNextAsync()).Should().BeTrue();
            iterator.Current.Should().Be(1);
            ids.Enumerations.Should().Be(1);
            fixture.Provider.Contexts.Single().WasDisposed.Should().BeFalse();
        }

        fixture.Provider.Contexts.Single().WasDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task MembershipAfterFromAndGroupingPreservesStageOrderAndOneSharedRequest()
    {
        await using var fixture = await Fixture.CreateAsync(entity =>
        {
            entity.HasVisibilityPolicy("Visible", row => row.Visible);
            entity.HasQuery("Report").From(sources => from row in sources.Set<MembershipRow>()
                                                      join other in sources.Set<MembershipRow>() on row.Id equals other.Id
                                                      select new { row.Id, other.Category })
                .WhereIn(row => row.Category, (MembershipReportRequest request) => request.Categories)
                .GroupBy(row => row.Category, group => new MembershipTotal { Category = group.Key, Count = group.Count() })
                .WhereIn(row => row.Count, (MembershipReportRequest request) => request.Counts)
                .OrderBy(total => total.Category).List();
        });
        var results = await fixture.Call<Task<IReadOnlyList<MembershipTotal>>>("ReportAsync", new MembershipReportRequest(["first", "second"], [2]));
        results.Select(total => (total.Category, total.Count)).Should().Equal(("first", 2));
        fixture.Sql.Commands.Single().Text.Should().Contain("INNER JOIN").And.Contain("GROUP BY").And.Contain("HAVING");
        fixture.Model.Entities.Single().Queries.Single().Parameters.Should().ContainSingle(parameter => parameter.Name == "request");
        fixture.Model.Entities.Single().Queries.Single().Pipeline!.Stages.Count(stage => stage.Kind == RepositoryQueryStageKind.Membership).Should().Be(2);
    }

    [Fact]
    public async Task MembershipWorksAfterProjectionWithPrunedDictionaryColumnsAndBoundApplicationPorts()
    {
        await using var fixture = await Fixture.CreateAsync(entity =>
        {
            entity.BindReadPort<IMembershipReadPort>();
            entity.HasQuery("Find").WhereIn(row => row.Id, (MembershipRequest request) => request.Ids)
                .OrderBy(row => row.Id).Select(row => row.Id).List();
            entity.HasQuery("Names").Select(row => new { row.Id, row.Name })
                .WhereIn(row => row.Id, (int[] ids) => ids).Dictionary(row => row.Id, row => row.Name)
                .ResultNullability<string>(value => value.Nullable());
        });
        ((IMembershipReadPort)fixture.Instance).Should().NotBeNull();
        (await ((IMembershipReadPort)fixture.Instance).FindAsync(new MembershipRequest([1, 3]))).Should().Equal(1, 3);
        var names = await fixture.Call<Task<IReadOnlyDictionary<int, string?>>>("NamesAsync", new[] { 1, 3 });
        names.Should().HaveCount(2);
        names[3].Should().BeNull();
        fixture.Sql.Commands.Last().Text.Should().NotContain("Category").And.NotContain("Visible");
    }

    [Fact]
    public async Task TypeAdaptersExpandInMembershipValuesBeforeNormalization()
    {
        await using var fixture = await Fixture.CreateAsync(entity => { }, customize: builder =>
        {
            var adapter = builder.HasTypeAdapter<int, MembershipId>(value => new MembershipId(value), value => value.Value);
            builder.Entity<MembershipRow>().HasQuery("Find")
                .WhereIn(row => row.Id, (MembershipId[] ids) => ids.Select(value => adapter.ToModel(value)), options => options.Distinct())
                .OrderBy(row => row.Id).Select(row => row.Id).List();
        });
        (await fixture.Read("FindAsync", new object[] { new[] { new MembershipId(2), new MembershipId(1) } })).Should().Equal(1, 2);
        fixture.Sql.Commands.Single().Values.Should().Contain("[2,1]");
    }

    [Fact]
    public async Task ClosedCachingUsesPreparedValuesAndInvalidInputCannotAcquireEvenOnACacheHit()
    {
        await using var fixture = await Fixture.CreateAsync(entity => entity.HasQuery("Find").Cache()
            .WhereIn(row => row.Id, (int[] ids) => ids, options => options.Distinct().MaximumCount(2))
            .Select(row => row.Id).List(), leases: true, cache: true);
        (await fixture.Read("FindAsync", new[] { 1, 2 })).Should().Equal(1, 2);
        await fixture.Read("FindAsync", new[] { 1, 2 });
        fixture.Sql.Commands.Should().ContainSingle();
        (await fixture.Read("FindAsync", new[] { 3 })).Should().Equal(3);
        fixture.Sql.Commands.Should().HaveCount(2);
        var acquisitions = fixture.Provider.Calls;
        Func<Task> invalid = async () => await fixture.Read("FindAsync", new[] { 1, 2, 2 });
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
        fixture.Provider.Calls.Should().Be(acquisitions);
    }

    [Theory]
    [InlineData("selectors")]
    [InlineData("cqrs")]
    public async Task LegacyCacheDecoratorsReusePreparedFiltersOnBothSelectorSurfaces(string surface)
    {
        await using var fixture = await Fixture.CreateAsync(entity => entity.HasQuery("Find").Cache()
            .WhereIn(row => row.Id, (int[] ids) => ids, options => options.Distinct()).OrderBy(row => row.Id).Select(row => row.Id).List(), surface, cache: true);
        (await fixture.Read("FindAsync", new[] { 2, 1, 2 })).Should().Equal(1, 2);
        await fixture.Read("FindAsync", new[] { 2, 1, 2 });
        fixture.Sql.Commands.Should().ContainSingle();
        (await fixture.Read("FindAsync", new[] { 3 })).Should().Equal(3);
        fixture.Sql.Commands.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("selectors")]
    [InlineData("cqrs")]
    [InlineData("closed")]
    public async Task OverloadedMembershipOperationsKeepTheirOwnManifestPoliciesAndCacheIdentity(string surface)
    {
        await using var fixture = await Fixture.CreateAsync(entity =>
        {
            entity.HasQuery("Find").Cache().Where(row => row.Id == 1)
                .WhereIn(row => row.Id, (MembershipFirstRequest request) => request.Ids, options => options.MaximumCount(3))
                .Select(row => row.Id).List();
            entity.HasQuery("Find").Cache().Where(row => row.Id == 2)
                .WhereIn(row => row.Id, (MembershipSecondRequest request) => request.Ids,
                    options => options.EmptyInput(RepositoryMembershipInputBehavior.IgnoreFilter).MaximumCount(4))
                .Select(row => row.Id).List();
        }, surface, cache: true);
        // Different CLR requests have identical serialized data. Operation identity must distinguish them.
        var first = new MembershipFirstRequest([1, 2]);
        var second = new MembershipSecondRequest([1, 2]);
        (await fixture.Read("FindAsync", first)).Should().Equal(1);
        (await fixture.Read("FindAsync", second)).Should().Equal(2);
        await fixture.Read("FindAsync", first);
        await fixture.Read("FindAsync", second);
        fixture.Sql.Commands.Should().HaveCount(2);
        using var manifest = System.Text.Json.JsonDocument.Parse(RepositoryManifestRenderer.Render(fixture.Model));
        var queries = manifest.RootElement.GetProperty("entities")[0].GetProperty("queries");
        queries[0].GetProperty("membershipInputs")[0].GetProperty("maximumRawCount").GetInt32().Should().Be(3);
        queries[1].GetProperty("membershipInputs")[0].GetProperty("maximumRawCount").GetInt32().Should().Be(4);
        queries[0].GetProperty("membershipInputs")[0].GetProperty("emptyInput").GetString().Should().Be("EmptySet");
        queries[1].GetProperty("membershipInputs")[0].GetProperty("emptyInput").GetString().Should().Be("IgnoreFilter");
    }

    [Fact]
    public async Task MembershipComposesOverARegisteredInfrastructureRootWithoutLeakingThatRoot()
    {
        await using var fixture = await Fixture.CreateAsync(entity => entity.HasQuery("Find")
            .FromProvider<MembershipRoot, MembershipRequest, MembershipRow>(request => request)
            .WhereIn(row => row.Id, (MembershipRequest request) => request.Ids)
            .OrderBy(row => row.Id).Select(row => row.Id).List());
        (await fixture.Read("FindAsync", new MembershipRequest([1, 3, 4]))).Should().Equal(1, 3);
        fixture.Sql.Commands.Should().ContainSingle();
        ClosedRepositorySourceRenderer.RenderContracts(fixture.Model).Should().NotContain("MembershipRoot").And.NotContain("IQueryable");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MembershipInputsCannotSneakEfEntitiesThroughClosedContracts(bool resultStage)
    {
        using var context = ModelContext();
        var builder = Builder("closed");
        var query = builder.Entity<MembershipRow>().HasQuery("Find");
        if (resultStage)
        {
            query.Select(row => row.Id).WhereIn(id => id, (MembershipRow[] rows) => rows.Select(row => row.Id)).List();
        }
        else
        {
            query.WhereIn(row => row.Id, (MembershipRow[] rows) => rows.Select(row => row.Id)).Select(row => row.Id).List();
        }

        Action generate = () => RepositoryModelFactory.Create(context, typeof(MembershipRow).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1502:*EF-free*");
    }

    [Fact]
    public async Task ZeroMaximumAllowsEmptySetsButRejectsAnyValue()
    {
        await using var fixture = await Fixture.CreateAsync(entity => entity.HasQuery("Find")
            .WhereIn(row => row.Id, (int[] ids) => ids, options => options.MaximumCount(0)).Select(row => row.Id).List(), leases: true);
        (await fixture.Read("FindAsync", Array.Empty<int>())).Should().BeEmpty();
        Func<Task> invalid = async () => await fixture.Read("FindAsync", new[] { 1 });
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
        fixture.Provider.Calls.Should().Be(1);
    }

    [Fact]
    public void CachedOneShotCollectionsAreRejectedDuringGeneration()
    {
        using var context = ModelContext();
        var builder = Builder("closed", cache: true);
        builder.Entity<MembershipRow>().HasQuery("Find").Cache().WhereIn(row => row.Id, (IEnumerable<int> ids) => ids).Select(row => row.Id).List();
        Action generate = () => RepositoryModelFactory.Create(context, typeof(MembershipRow).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1903:*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueryableValuesAndCharacterStringInputsAreRejectedWithoutEnumerating(bool queryable)
    {
        using var context = ModelContext();
        var builder = Builder("selectors");
        if (queryable)
        {
            builder.Entity<MembershipRow>().HasQuery("Find").WhereIn(row => row.Id, (IQueryable<int> ids) => ids).List();
        }
        else
        {
            builder.Entity<MembershipRow>().HasQuery("Find").WhereIn(row => row.Name![0], (string names) => names).List();
        }

        Action generate = () => RepositoryModelFactory.Create(context, typeof(MembershipRow).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1901:*");
    }

    [Fact]
    public async Task HiddenQueryableValuesAreRejectedBeforeContextAcquisition()
    {
        await using var fixture = await Fixture.CreateAsync(entity => entity.HasQuery("Find")
            .WhereIn(row => row.Id, (IEnumerable<int> ids) => ids).Select(row => row.Id).List(), leases: true);
        Func<Task> invalid = async () => await fixture.Read("FindAsync", new[] { 1 }.AsQueryable());
        await invalid.Should().ThrowAsync<ArgumentException>().WithMessage("*in-memory*");
        fixture.Provider.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("closed", RepositoryOutputLayout.SingleFile)]
    [InlineData("closed", RepositoryOutputLayout.PerContract)]
    [InlineData("cqrs", RepositoryOutputLayout.PerEntity)]
    [InlineData("selectors", RepositoryOutputLayout.PerEntity)]
    public void AllLayoutsAndIsolatedNamespacesGenerateDeterministically(string surface, RepositoryOutputLayout layout)
    {
        using var context = ModelContext();
        var builder = Builder(surface);
        builder.Configure(options => options.UseGeneratedNamespace("Membership.Generated"));
        builder.Entity<MembershipRow>().HasQuery("Find").WhereIn(row => row.Id, (int[] ids) => ids).Select(row => row.Id).List();
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        var repeat = RepositoryGenerator.Generate(context, builder.Build(), layout);
        bundle.Files.Select(file => (file.Name, file.Contents)).Should().Equal(repeat.Files.Select(file => (file.Name, file.Contents)));
        ClosedRepositoryTests.Compile(bundle.Model, rejectWarnings: true);
        var manifest = RepositoryManifestRenderer.Render(bundle.Model);
        manifest.Should().Contain("membershipInputs").And.Contain("maximumRawCount").And.Contain("Reject").And.Contain("EmptySet");
        if (surface == "closed")
        {
            ClosedRepositorySourceRenderer.RenderContracts(bundle.Model).Should().NotContain("MembershipApply").And.NotContain("MembershipValues");
        }
    }

    [Fact]
    public void MembershipHelpersAreAbsentUnlessConfiguredAndDoNotExpandSimpleDefaults()
    {
        using var context = ModelContext();
        var builder = Builder("selectors");
        builder.Entity<MembershipRow>().HasQuery("Find").Count();
        var model = RepositoryModelFactory.Create(context, typeof(MembershipRow).Assembly, true, builder.Build());
        model.Entities.Single().Queries.Single().Memberships.Should().BeNull();
        ProjectionSourceRenderer.Render(model).Should().NotContain("__repoGenMembership");
        RepositoryManifestRenderer.Render(model).Should().NotContain("membershipInputs");
    }

    [Fact]
    public void StaleEntityAndResultBuildersCannotMisplaceMembership()
    {
        var builder = Builder("closed");
        var query = builder.Entity<MembershipRow>().HasQuery("Find");
        var result = query.Select(row => new { row.Id });
        Action staleEntity = () => query.WhereIn(row => row.Id, (int[] ids) => ids);
        staleEntity.Should().Throw<InvalidOperationException>().WithMessage("RGD1902:*");
        result.Select(row => row.Id);
        Action staleResult = () => result.WhereIn(row => row.Id, (int[] ids) => ids);
        staleResult.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        var from = builder.Entity<MembershipRow>().HasQuery("From").WhereIn(row => row.Id, (int[] ids) => ids);
        Action replaced = () => from.From(sources => sources.Set<MembershipRow>());
        replaced.Should().Throw<InvalidOperationException>().WithMessage("*replaces*root*");
    }

    [Fact]
    public void InvalidOptionsFailAtRegistrationAndZeroIsAValidMaximum()
    {
        var builder = Builder("closed");
        var query = builder.Entity<MembershipRow>().HasQuery("Find");
        Action count = () => query.WhereIn(row => row.Id, (int[] ids) => ids, options => options.MaximumCount(-1));
        count.Should().Throw<ArgumentOutOfRangeException>();
        Action nullInput = () => query.WhereIn(row => row.Id, (int[] ids) => ids, options => options.NullInput((RepositoryMembershipInputBehavior)99));
        nullInput.Should().Throw<ArgumentOutOfRangeException>();
        Action emptyInput = () => query.WhereIn(row => row.Id, (int[] ids) => ids, options => options.EmptyInput((RepositoryMembershipInputBehavior)99));
        emptyInput.Should().Throw<ArgumentOutOfRangeException>();
        Action nullValues = () => query.WhereIn(row => row.Id, (int[] ids) => ids, options => options.NullValues((RepositoryMembershipNullValueBehavior)99));
        nullValues.Should().Throw<ArgumentOutOfRangeException>();
        query.WhereIn(row => row.Id, (int[] ids) => ids, options => options.MaximumCount(0));
        builder.Build().Entities.Single().Queries.Single().Memberships.Single().Options.MaximumCount.Should().Be(0);
    }

    internal static RepositoryModelBuilder Builder(string surface, bool leases = false, bool cache = false)
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options =>
        {
            options.UseSimpleDefaults().GenerateOnlyConfiguredEntities();
            if (surface == "closed")
            {
                options.UseClosedRepositories("Membership.Contracts").UseAsyncContextLeases(leases);
            }

            if (surface == "cqrs")
            {
                options.UseCqrsSelectors();
            }

            if (cache && surface == "closed")
            {
                options.UseClosedCaching("membership:fixture");
            }
            else if (cache)
            {
                options.GenerateCacheDecorator();
            }
        });
        if (surface != "selectors")
        {
            builder.Entity<MembershipRow>().WriteEnabled = false;
        }

        return builder;
    }

    private static MembershipContext ModelContext() => new(new DbContextOptionsBuilder<MembershipContext>().UseSqlite("Data Source=:memory:").Options);

    private sealed class Fixture(
        SqliteConnection connection,
        MembershipContext context,
        RepositoryGenerationModel model,
        object instance,
        Type? extensions,
        MembershipSql sql,
        Provider provider,
        MemoryCache memory) : IAsyncDisposable
    {
        public MembershipContext Context { get; } = context;
        public RepositoryGenerationModel Model { get; } = model;
        public object Instance { get; } = instance;
        public MembershipSql Sql { get; } = sql;
        public Provider Provider { get; } = provider;

        public Task<IReadOnlyList<int>> Read(string method, params object?[] arguments) => Call<Task<IReadOnlyList<int>>>(method, arguments);
        public T Call<T>(string method, params object?[] arguments) => CallWithToken<T>(method, CancellationToken.None, arguments);

        public T CallWithToken<T>(string method, CancellationToken token, params object?[] arguments)
        {
            try
            {
                var parameters = arguments.Append((object)token).ToArray();
                var invocation = extensions is null ? parameters : parameters.Prepend(Instance).ToArray();
                var target = (extensions ?? Instance.GetType()).GetMethods().Single(candidate => candidate.Name == method &&
                                                                                                 candidate.GetParameters().Length == invocation.Length && candidate.GetParameters().Select((parameter, index) =>
                                                                                                     invocation[index] is null || parameter.ParameterType.IsInstanceOfType(invocation[index])).All(matches => matches));
                return (T)target.Invoke(extensions is null ? Instance : null, invocation)!;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        public static async Task<Fixture> CreateAsync(Action<RepositoryEntityBuilder<MembershipRow>> configure, string surface = "closed",
            bool leases = false, bool cache = false, Action<RepositoryModelBuilder>? customize = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var sql = new MembershipSql();
            var context = new MembershipContext(new DbContextOptionsBuilder<MembershipContext>().UseSqlite(connection).AddInterceptors(sql).Options);
            await context.Database.EnsureCreatedAsync();
            context.Rows.AddRange(new MembershipRow { Id = 1, Name = "A", Category = "first", Visible = true },
                new MembershipRow { Id = 2, Name = "B", Category = "first", Visible = true },
                new MembershipRow { Id = 3, Name = null, Category = "second", Visible = true },
                new MembershipRow { Id = 4, Name = "hidden", Category = "first", Visible = false });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            sql.Commands.Clear();
            var builder = Builder(surface, leases, cache);
            configure(builder.Entity<MembershipRow>());
            customize?.Invoke(builder);
            var model = RepositoryModelFactory.Create(context, typeof(MembershipRow).Assembly, true, builder.Build());
            var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
            var prefix = typeof(MembershipRow).Namespace + ".MembershipRow";
            var provider = new Provider(connection, sql);
            var memory = new MemoryCache(new MemoryCacheOptions());
            object instance;
            Type? extensions = null;
            if (surface == "closed")
            {
                object source = leases ? provider : context;
                object[] constructorArguments = cache ? [source, new MemoryRepositoryResultCache(memory)] : [source];
                var roots = model.Entities.Single().Queries.Select(query => query.RegisteredRoot).OfType<ConfiguredRegisteredQueryModel>()
                    .DistinctBy(root => root.FieldName).Select(root => Activator.CreateInstance(typeof(MembershipRow).Assembly.GetType(root.ProviderTypeName["global::".Length..])!)!);
                instance = Activator.CreateInstance(assembly.GetType(prefix + "RepositoryOperations")!, constructorArguments.Concat(roots).ToArray())!;
            }
            else
            {
                instance = Activator.CreateInstance(assembly.GetType(prefix + "Repository")!, context)!;
                if (cache)
                {
                    var cacheProvider = DispatchProxy.Create(assembly.GetType("RonSijm.RepoGen.IRepositoryCacheProvider")!, typeof(MultiContextCacheProxy));
                    instance = Activator.CreateInstance(assembly.GetType(prefix + "RepositoryCacheDecorator")!, instance, cacheProvider)!;
                }

                if (surface == "cqrs")
                {
                    instance = Activator.CreateInstance(assembly.GetType(prefix + "ReadRepository")!, instance)!;
                }
                else
                {
                    extensions = assembly.GetType(model.Entities.Single().Queries[0].Namespace + ".MembershipRowConfiguredQueryExtensions");
                }
            }

            return new(connection, context, model, instance, extensions, sql, provider, memory);
        }

        public async ValueTask DisposeAsync()
        {
            memory.Dispose();
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class Provider(SqliteConnection connection, MembershipSql sql) : IDbContextLeaseProvider<MembershipContext>
    {
        public int Calls { get; private set; }
        public List<MembershipContext> Contexts { get; } = [];

        public ValueTask<DbContextLease<MembershipContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            var context = new MembershipContext(new DbContextOptionsBuilder<MembershipContext>().UseSqlite(connection).AddInterceptors(sql).Options);
            Contexts.Add(context);
            return ValueTask.FromResult(DbContextLease.Owned(context));
        }
    }
}

public sealed class MembershipRow
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string Category { get; set; } = "";
    public bool Visible { get; set; }
}

public sealed class MembershipContext(DbContextOptions<MembershipContext> options) : DbContext(options)
{
    public DbSet<MembershipRow> Rows => Set<MembershipRow>();
    public bool WasDisposed { get; private set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<MembershipRow>().Property(row => row.Id).ValueGeneratedNever();

    public override ValueTask DisposeAsync()
    {
        WasDisposed = true;
        return base.DisposeAsync();
    }
}

public sealed record MembershipRequest(int[]? Ids);

public sealed record MembershipFirstRequest(int[] Ids);

public sealed record MembershipSecondRequest(int[] Ids);

public sealed record MembershipReportRequest(string[] Categories, int[] Counts);

public readonly record struct MembershipId(int Value);

public sealed class MembershipTotal
{
    public string Category { get; set; } = "";
    public int Count { get; set; }
}

public interface IMembershipReadPort
{
    Task<IReadOnlyList<int>> FindAsync(MembershipRequest request, CancellationToken cancellationToken = default);
}

public sealed class MembershipRoot : IRepositoryQueryRoot<MembershipContext, MembershipRequest, MembershipRow>
{
    public IQueryable<MembershipRow> CreateQuery(MembershipContext context, MembershipRequest request) => context.Rows.AsNoTracking().Where(row => row.Visible);
}

internal sealed class MembershipSql : DbCommandInterceptor
{
    public List<(string Text, object?[] Values)> Commands { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).ToArray()));
        return ValueTask.FromResult(result);
    }
}

internal sealed class MembershipOneShot(int[] values, Action? beforeYield = null) : IEnumerable<int>
{
    public int Enumerations { get; private set; }
    public int Yields { get; private set; }
    public int Disposals { get; private set; }

    public IEnumerator<int> GetEnumerator()
    {
        if (++Enumerations != 1)
        {
            throw new InvalidOperationException("A one-shot input was enumerated again.");
        }

        try
        {
            foreach (var value in values)
            {
                beforeYield?.Invoke();
                Yields++;
                yield return value;
            }
        }
        finally { Disposals++; }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}