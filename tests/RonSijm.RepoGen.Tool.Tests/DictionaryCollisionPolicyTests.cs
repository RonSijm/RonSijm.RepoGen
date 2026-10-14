using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

public sealed class DictionaryCollisionPolicyTests
{
    [Theory]
    [InlineData("key")]
    [InlineData("value")]
    [InlineData("negative")]
    [InlineData("large")]
    public void InvalidArgumentsCannotReplaceAnExistingTerminal(string argument)
    {
        var builder = Builder();
        var query = builder.Entity<CommandRow>().HasQuery("Read").Results<CommandRow>().List();
        Expression<Func<CommandRow, int>> key = argument == "key" ? null! : row => row.OwnerId;
        Expression<Func<CommandRow, string>> value = argument == "value" ? null! : row => row.Name;
        var policy = argument == "negative" ? (RepositoryDuplicateKeyBehavior)(-1) : argument == "large" ? (RepositoryDuplicateKeyBehavior)42 : RepositoryDuplicateKeyBehavior.KeepFirst;
        Action configure = () => query.Dictionary(key, value, policy);
        if (argument is "key" or "value")
        {
            configure.Should().Throw<ArgumentNullException>();
        }
        else
        {
            configure.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*RGD2501*");
        }

        builder.Build().Entities.Single().Queries.Single().ResultKind.Should().Be(RepositoryQueryResultKind.List);
        builder.Build().Entities.Single().Queries.Single().Dictionary.Should().BeNull();
    }

    [Fact]
    public void StaleBuildersCannotConfigureCollisionPoliciesAndChangingTerminalsClearsThem()
    {
        var builder = Builder();
        var query = builder.Entity<CommandRow>().HasQuery("Read").Results<CommandRow>();
        var projected = query.Select(row => row.Name);
        Action stale = () => query.Dictionary(row => row.OwnerId, RepositoryDuplicateKeyBehavior.KeepLast);
        stale.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        projected.OrderBy(name => name).Dictionary(name => name, RepositoryDuplicateKeyBehavior.KeepFirst);
        builder.Build().Entities.Single().Queries.Single().Dictionary!.DuplicateKeys.Should().Be(RepositoryDuplicateKeyBehavior.KeepFirst);
        projected.Lookup(name => name);
        builder.Build().Entities.Single().Queries.Single().Dictionary!.DuplicateKeys.Should().Be(RepositoryDuplicateKeyBehavior.Throw);
        projected.List();
        builder.Build().Entities.Single().Queries.Single().Dictionary.Should().BeNull();
    }

    [Theory]
    [InlineData("unordered")]
    [InlineData("conditional")]
    [InlineData("distinct")]
    [InlineData("conditionalDistinct")]
    [InlineData("group")]
    [InlineData("from")]
    [InlineData("registered")]
    [InlineData("disabledDefault")]
    public void FirstAndLastRequireOrderingOnEveryBranch(string scenario)
    {
        using var context = MetadataContext();
        var builder = Builder();
        var entity = builder.Entity<CommandRow>();
        RepositoryResultQueryBuilder<int> query;
        if (scenario == "group")
        {
            query = entity.HasQuery("Read").GroupBy(row => row.OwnerId, group => group.Count()).Results<int>();
        }
        else if (scenario == "from")
        {
            query = entity.HasQuery("Read").From(sources => sources.Set<CommandRow>().OrderBy(row => row.Id).Select(row => row.Id));
        }
        else if (scenario == "registered")
        {
            query = entity.HasQuery("Read").FromProvider<DictionaryQueryRoot, DictionaryPolicyRequest, DictionaryPolicyRow>(request => request).Select(row => row.Id);
        }
        else
        {
            var root = entity.HasQuery("Read");
            if (scenario == "disabledDefault")
            {
                entity.HasDefaultSort(row => row.Id);
                root.UseDefaultSort(false);
            }

            query = root.Select(row => row.Id);
        }

        if (scenario == "conditional")
        {
            query.OrderByIf((DictionaryPolicyRequest request) => request.Descending, id => id);
        }
        else if (scenario == "distinct")
        {
            query.OrderBy(id => id).Distinct();
        }
        else if (scenario == "conditionalDistinct")
        {
            query.OrderBy(id => id).DistinctIf<DictionaryPolicyRequest>(request => request.Descending);
        }

        query.Dictionary(id => id, RepositoryDuplicateKeyBehavior.KeepFirst);
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2502:*guaranteed on every branch*");
    }

    [Theory]
    [InlineData("result")]
    [InlineData("source")]
    [InlineData("default")]
    [InlineData("fallback")]
    [InlineData("afterDistinct")]
    public void ExplicitAndFallbackOrderingAreRecognized(string scenario)
    {
        using var context = MetadataContext();
        var builder = Builder();
        var entity = builder.Entity<CommandRow>();
        var root = entity.HasQuery("Read");
        if (scenario == "source")
        {
            root.OrderBy(row => row.Id);
        }

        if (scenario == "default")
        {
            entity.HasDefaultSort(row => row.Id);
        }

        var query = root.Results<CommandRow>();
        if (scenario is "result" or "fallback")
        {
            query.OrderBy(row => row.Id);
        }

        if (scenario == "fallback")
        {
            query.OrderByDescendingIf((DictionaryPolicyRequest request) => request.Descending, row => row.Id);
        }

        if (scenario == "afterDistinct")
        {
            query.Select(row => row.OwnerId).Distinct().OrderBy(id => id).Dictionary(id => id, RepositoryDuplicateKeyBehavior.KeepLast);
        }
        else
        {
            query.Dictionary(row => row.OwnerId, row => row.Name, RepositoryDuplicateKeyBehavior.KeepLast);
        }

        Model(context, builder).Entities.Single().Queries.Single().Dictionary!.DuplicateKeys.Should().Be(RepositoryDuplicateKeyBehavior.KeepLast);
    }

    [Theory]
    [InlineData("closed", RepositoryOutputLayout.SingleFile)]
    [InlineData("closed", RepositoryOutputLayout.PerEntity)]
    [InlineData("closed", RepositoryOutputLayout.PerContract)]
    [InlineData("cqrs", RepositoryOutputLayout.SingleFile)]
    [InlineData("cqrs", RepositoryOutputLayout.PerEntity)]
    [InlineData("selectors", RepositoryOutputLayout.SingleFile)]
    [InlineData("selectors", RepositoryOutputLayout.PerEntity)]
    [InlineData("cachedSelectors", RepositoryOutputLayout.SingleFile)]
    [InlineData("cachedSelectors", RepositoryOutputLayout.PerEntity)]
    public void AllSurfacesAndLayoutsCompileStrictlyWithBothPolicies(string surface, RepositoryOutputLayout layout)
    {
        using var context = MetadataContext();
        var builder = Builder(surface);
        var entity = builder.Entity<CommandRow>();
        foreach (var behavior in Enum.GetValues<RepositoryDuplicateKeyBehavior>())
        {
            var root = entity.HasQuery(behavior.ToString());
            if (surface == "cachedSelectors")
            {
                root.Cache();
            }

            root.Results<CommandRow>().OrderBy(row => row.Id).Dictionary(row => row.OwnerId, row => row.Description, behavior).ResultNullability<string>(type => type.Nullable());
            entity.HasQuery(behavior + "Rows").Select(row => new DictionaryPolicyRow { Id = row.Id, OwnerId = row.OwnerId, Name = row.Name })
                .OrderBy(row => row.Id).Dictionary(row => row.OwnerId, behavior);
        }

        var model = Model(context, builder);
        var result = RepositoryGenerator.Generate(model, layout);
        result.Files.Should().Equal(RepositoryGenerator.Generate(model, layout).Files);
        CompileFiles(result.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts or RepositoryOutputKind.Projections).Select(file => file.Contents));
    }

    [Fact]
    public void ExplicitThrowRetainsByteIdenticalOutputWhilePoliciesChangeOnlyBehaviorAndManifest()
    {
        using var context = MetadataContext();
        var builder = Builder();
        var root = builder.Entity<CommandRow>().HasQuery("Read").Cache();
        builder.Configure(options => options.UseClosedCaching("dictionary-fixture").IncludeApiDataContracts());
        var query = root.Results<CommandRow>().OrderBy(row => row.Id).Dictionary(row => row.OwnerId, row => row.Name);
        var original = Model(context, builder);
        var originalFiles = RepositoryGenerator.Generate(original).Files;
        var json = JsonSerializer.Serialize(original.Entities.Single().Queries.Single());
        json.Should().NotContain("DuplicateKeys");
        query.Dictionary(row => row.OwnerId, row => row.Name, RepositoryDuplicateKeyBehavior.Throw);
        RepositoryGenerator.Generate(Model(context, builder)).Files.Should().Equal(originalFiles);
        query.Dictionary(row => row.OwnerId, row => row.Name, RepositoryDuplicateKeyBehavior.KeepFirst);
        var first = Model(context, builder);
        query.Dictionary(row => row.OwnerId, row => row.Name, RepositoryDuplicateKeyBehavior.KeepLast);
        var last = Model(context, builder);
        RepositoryApiSnapshot.Capture(first).ToJson().Should().Be(RepositoryApiSnapshot.Capture(original).ToJson()).And.Be(RepositoryApiSnapshot.Capture(last).ToJson());
        ClosedRepositorySourceRenderer.RenderContracts(first).Should().Be(ClosedRepositorySourceRenderer.RenderContracts(original));
        JsonSerializer.Serialize(first.Entities.Single().Queries.Single()).Should().NotBe(json).And.NotBe(JsonSerializer.Serialize(last.Entities.Single().Queries.Single()));
        JsonNode.Parse(RepositoryManifestRenderer.Render(original))!["entities"]![0]!["queries"]![0]!["dictionaryDuplicateKeys"].Should().BeNull();
        JsonNode.Parse(RepositoryManifestRenderer.Render(first))!["entities"]![0]!["queries"]![0]!["dictionaryDuplicateKeys"]!.GetValue<string>().Should().Be("KeepFirst");
        JsonNode.Parse(RepositoryManifestRenderer.Render(last))!["entities"]![0]!["queries"]![0]!["dictionaryDuplicateKeys"]!.GetValue<string>().Should().Be("KeepLast");
        first.Entities.Single().Queries.Single().Dictionary!.DuplicateKeys.Should().Be(RepositoryDuplicateKeyBehavior.KeepFirst);
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("cqrs")]
    [InlineData("selectors")]
    public async Task PoliciesSelectDeclaredRowsAndOnlyApprovedColumnsInOneSqlCommand(string surface)
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder(surface);
        var entity = builder.Entity<CommandRow>();
        entity.HasVisibilityPolicy("Visible", row => row.Id < 5);
        foreach (var policy in Enum.GetValues<RepositoryDuplicateKeyBehavior>())
        {
            entity.HasQuery(policy.ToString()).Results<CommandRow>().OrderBy(row => row.Id)
                .OrderByDescendingIf((DictionaryPolicyRequest request) => request.Descending, row => row.Id)
                .Dictionary(row => row.OwnerId, row => row.Name, policy);
        }

        var instance = Create(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), fixture.Context, surface);
        var plain = new DictionaryPolicyRequest(false);
        var descending = new DictionaryPolicyRequest(true);
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(instance, "KeepFirstAsync", plain, CancellationToken.None))[1].Should().Be("Alpha");
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(instance, "KeepLastAsync", plain, CancellationToken.None))[1].Should().Be("Gamma");
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(instance, "KeepFirstAsync", descending, CancellationToken.None))[1].Should().Be("Gamma");
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(instance, "KeepLastAsync", descending, CancellationToken.None))[1].Should().Be("Alpha");
        fixture.Sql.Commands.Should().HaveCount(4).And.OnlyContain(command => command.Sql.Contains("ORDER BY") && !command.Sql.Contains("Description") && !command.Sql.Contains("Normalized"));
        Func<Task> duplicates = async () => await Invoke<Task<IReadOnlyDictionary<int, string>>>(instance, "ThrowAsync", plain, CancellationToken.None);
        await duplicates.Should().ThrowAsync<ArgumentException>();
        fixture.Sql.Commands.Should().HaveCount(5);
        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
        if (surface == "closed")
        {
            fixture.Context.Database.GetCommandTimeout().Should().Be(17);
        }
    }

    [Fact]
    public async Task RowOverloadEmptyUniqueNullableValuesAndClrKeyEqualityKeepTheirContracts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        var entity = builder.Entity<CommandRow>();
        entity.HasQuery("Rows").Select(row => new DictionaryPolicyRow { Id = row.Id, OwnerId = row.OwnerId, Name = row.Name }).OrderBy(row => row.Id)
            .Dictionary(row => row.OwnerId, RepositoryDuplicateKeyBehavior.KeepLast);
        entity.HasQuery("Empty").Where(row => row.Id < 0).Results<CommandRow>().OrderBy(row => row.Id)
            .Dictionary(row => row.OwnerId, row => row.Name, RepositoryDuplicateKeyBehavior.KeepFirst);
        entity.HasQuery("Nullable").Results<CommandRow>().OrderBy(row => row.Id)
            .Dictionary(row => row.OwnerId, row => row.Description, RepositoryDuplicateKeyBehavior.KeepLast).ResultNullability<string>(type => type.Nullable());
        entity.HasQuery("CaseKeys").Results<CommandRow>().OrderBy(row => row.Id)
            .Dictionary(row => row.Id == 1 ? "A" : "a", row => row.Name, RepositoryDuplicateKeyBehavior.KeepFirst);
        entity.HasQuery("Unique").Results<CommandRow>().Dictionary(row => row.Id, row => row.Name);
        var instance = Create(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), fixture.Context);
        (await Invoke<Task<IReadOnlyDictionary<int, DictionaryPolicyRow>>>(instance, "RowsAsync", CancellationToken.None))[1].Id.Should().Be(5);
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(instance, "EmptyAsync", CancellationToken.None)).Should().BeEmpty();
        (await Invoke<Task<IReadOnlyDictionary<int, string?>>>(instance, "NullableAsync", CancellationToken.None))[1].Should().BeNull();
        var cases = await Invoke<Task<IReadOnlyDictionary<string, string>>>(instance, "CaseKeysAsync", CancellationToken.None);
        cases.Should().HaveCount(2);
        cases["A"].Should().Be("Alpha");
        cases["a"].Should().Be("Beta");
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(instance, "UniqueAsync", CancellationToken.None)).Should().HaveCount(5);
        fixture.Sql.Commands.Should().HaveCount(5);
    }

    [Fact]
    public async Task JoinedGroupedDistinctAndCappedPipelinesApplyPolicyAtTheFinalDictionaryBoundary()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        var entity = builder.Entity<CommandRow>();
        entity.HasQuery("Join").From(sources => from row in sources.Set<CommandRow>() join owner in sources.Set<CommandOwner>() on row.OwnerId equals owner.Id select new { row.Id, OwnerId = owner.Id, row.Name })
            .OrderBy(row => row.Id).Dictionary(row => row.OwnerId, row => row.Name, RepositoryDuplicateKeyBehavior.KeepLast);
        // Group projections remain typed, and multiple groups may deliberately share a dictionary key.
        entity.HasQuery("Grouped").GroupBy(row => row.OwnerId, group => new DictionaryPolicyCount { Owner = group.Key, Count = group.Count() }).Results<DictionaryPolicyCount>()
            .OrderBy(group => group.Owner).Dictionary(group => 0, group => group.Count, RepositoryDuplicateKeyBehavior.KeepLast);
        entity.HasQuery("Capped").Results<CommandRow>().OrderBy(row => row.Id).Take(3)
            .Dictionary(row => row.OwnerId, row => row.Name, RepositoryDuplicateKeyBehavior.KeepLast);
        entity.HasQuery("Distinct").Select(row => row.OwnerId).Distinct().OrderBy(id => id)
            .Dictionary(id => id, RepositoryDuplicateKeyBehavior.KeepFirst);
        var instance = Create(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), fixture.Context);
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(instance, "JoinAsync", CancellationToken.None))[1].Should().Be("Hidden");
        (await Invoke<Task<IReadOnlyDictionary<int, int>>>(instance, "GroupedAsync", CancellationToken.None))[0].Should().Be(1);
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(instance, "CappedAsync", CancellationToken.None))[1].Should().Be("Gamma");
        (await Invoke<Task<IReadOnlyDictionary<int, int>>>(instance, "DistinctAsync", CancellationToken.None)).Keys.Should().BeEquivalentTo([1, 2]);
        fixture.Sql.Commands.Should().HaveCount(4);
        fixture.Sql.Commands[0].Sql.Should().Contain("JOIN");
        fixture.Sql.Commands[1].Sql.Should().Contain("GROUP BY");
        fixture.Sql.Commands[2].Sql.Should().Contain("LIMIT");
        fixture.Sql.Commands[3].Sql.Should().Contain("DISTINCT");
    }

    [Fact]
    public async Task RegisteredRootsAndWriteSideReadsParticipateInSharedWork()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases());
        var entity = builder.Entity<CommandRow>();
        entity.HasQuery("Registered").FromProvider<DictionaryQueryRoot, DictionaryPolicyRequest, DictionaryPolicyRow>(request => request).OrderBy(row => row.Id)
            .Dictionary(row => row.OwnerId, row => row.Name, RepositoryDuplicateKeyBehavior.KeepFirst);
        entity.HasQuery("ForCommand").OnWriteRepository().Results<CommandRow>().OrderBy(row => row.Id)
            .Dictionary(row => row.OwnerId, row => row.Name, RepositoryDuplicateKeyBehavior.KeepLast);
        var root = new DictionaryQueryRoot();
        await using var work = new RepositoryUnitOfWork<CommandContext>(new ScopedDbContextLeaseProvider<CommandContext>(fixture.Context));
        var assembly = ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true);
        var operations = Activator.CreateInstance(TypeOf(assembly, "CommandRowRepositoryOperations"), work, root)!;
        await using var transaction = await work.BeginTransactionAsync();
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(operations, "RegisteredAsync", new DictionaryPolicyRequest(false), CancellationToken.None))[1].Should().Be("Alpha");
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(operations, "ForCommandAsync", CancellationToken.None))[1].Should().Be("Hidden");
        root.SawTransaction.Should().BeTrue();
        fixture.Sql.Commands.Should().HaveCount(2).And.OnlyContain(command => command.Transaction && command.Timeout == 3);
        await transaction.RollbackAsync();
        fixture.Context.Database.GetCommandTimeout().Should().Be(17);
        fixture.Context.DisposeCount.Should().Be(0);
    }

    [Theory]
    [InlineData(RepositoryDuplicateKeyBehavior.Throw)]
    [InlineData(RepositoryDuplicateKeyBehavior.KeepFirst)]
    [InlineData(RepositoryDuplicateKeyBehavior.KeepLast)]
    public async Task NullKeysAreNeverAcceptedAndOwnedContextsRestoreTheirTimeoutOnFailure(RepositoryDuplicateKeyBehavior policy)
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases());
        builder.Entity<CommandRow>().HasQuery("Null").Results<CommandRow>().OrderBy(row => row.Id)
            .Dictionary(row => DictionaryPolicyKeys.MalformedKey(row.Description), row => row.Name, policy);
        var provider = new OwnedProvider(fixture.Options);
        var instance = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "CommandRowRepositoryOperations"), provider)!;
        Func<Task> execute = async () => await Invoke<Task<IReadOnlyDictionary<string, string>>>(instance, "NullAsync", CancellationToken.None);
        await execute.Should().ThrowAsync<ArgumentNullException>();
        provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
        provider.Contexts[0].TimeoutOnDispose.Should().Be(17);
    }

    [Theory]
    [InlineData(RepositoryDuplicateKeyBehavior.KeepFirst, 1)]
    [InlineData(RepositoryDuplicateKeyBehavior.KeepLast, 1)]
    [InlineData(RepositoryDuplicateKeyBehavior.KeepFirst, 5)]
    [InlineData(RepositoryDuplicateKeyBehavior.KeepLast, 5)]
    public async Task CancellationDuringDictionaryAssemblyDoesNotReturnPartialResults(RepositoryDuplicateKeyBehavior policy, int hashNumber)
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases());
        builder.Entity<CommandRow>().HasQuery("Read").Results<CommandRow>().OrderBy(row => row.Id)
            .Dictionary(row => new DictionaryCancelKey(row.OwnerId), row => row.Name, policy);
        var provider = new OwnedProvider(fixture.Options);
        var instance = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "CommandRowRepositoryOperations"), provider)!;
        using var cancellation = new CancellationTokenSource();
        DictionaryCancelKey.CancelOnHash(cancellation, hashNumber);
        try
        {
            Func<Task> execute = async () => await Invoke<Task<IReadOnlyDictionary<DictionaryCancelKey, string>>>(instance, "ReadAsync", cancellation.Token);
            await execute.Should().ThrowAsync<OperationCanceledException>();
            DictionaryCancelKey.HashCount.Should().Be(hashNumber);
        }
        finally { DictionaryCancelKey.CancelOnHash(null); }

        provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
        provider.Contexts[0].TimeoutOnDispose.Should().Be(17);
    }

    [Fact]
    public async Task ClosedCachedPoliciesHaveDifferentShapeKeysAndReturnCloneSafeDictionaries()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryRepositoryResultCache(memory);

        object Operations(RepositoryDuplicateKeyBehavior policy)
        {
            var builder = Builder();
            builder.Configure(options => options.UseClosedCaching("dictionary-fixture"));
            builder.Entity<CommandRow>().HasQuery("Read").Cache().Results<CommandRow>().OrderBy(row => row.Id)
                .Dictionary(row => row.OwnerId, row => row.Name, policy);
            return Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "CommandRowRepositoryOperations"), fixture.Context, cache)!;
        }

        var first = Operations(RepositoryDuplicateKeyBehavior.KeepFirst);
        var last = Operations(RepositoryDuplicateKeyBehavior.KeepLast);
        var firstResult = await Invoke<Task<IReadOnlyDictionary<int, string>>>(first, "ReadAsync", CancellationToken.None);
        firstResult[1].Should().Be("Alpha");
        ((Dictionary<int, string>)firstResult)[1] = "caller mutation";
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(last, "ReadAsync", CancellationToken.None))[1].Should().Be("Hidden");
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands.Clear();
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(first, "ReadAsync", CancellationToken.None))[1].Should().Be("Alpha");
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(last, "ReadAsync", CancellationToken.None))[1].Should().Be("Hidden");
        fixture.Sql.Commands.Should().BeEmpty();
    }

    [Fact]
    public void LegacyCacheIdentityIncludesNonDefaultPolicyWithoutChangingDefaultOutput()
    {
        using var context = MetadataContext();
        var builder = Builder("cachedSelectors");
        var query = builder.Entity<CommandRow>().HasQuery("Read").Cache().Results<CommandRow>().OrderBy(row => row.Id);
        query.Dictionary(row => row.OwnerId, row => row.Name);
        var original = ProjectionSourceRenderer.Render(Model(context, builder));
        query.Dictionary(row => row.OwnerId, row => row.Name, RepositoryDuplicateKeyBehavior.Throw);
        ProjectionSourceRenderer.Render(Model(context, builder)).Should().Be(original);
        query.Dictionary(row => row.OwnerId, row => row.Name, RepositoryDuplicateKeyBehavior.KeepFirst);
        var first = ProjectionSourceRenderer.Render(Model(context, builder));
        query.Dictionary(row => row.OwnerId, row => row.Name, RepositoryDuplicateKeyBehavior.KeepLast);
        var last = ProjectionSourceRenderer.Render(Model(context, builder));
        first.Should().Contain("var cacheKey = \"ReadAsync:").And.NotBe(original).And.NotBe(last);
        first.Split('\n').Single(line => line.Contains("var cacheKey", StringComparison.Ordinal)).Should().NotBe(last.Split('\n').Single(line => line.Contains("var cacheKey", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TypedKeyAdaptersAndExistingApplicationPortsRetainTheirShapes()
    {
        await using var context = TypeAdapterTests.Context();
        await TypeAdapterTests.Seed(context);
        var builder = TypeAdapterTests.Builder(closed: true);
        var id = builder.HasTypeAdapter<int, ApplicationRowId>(value => new ApplicationRowId(value), value => value.Value);
        var entity = builder.Entity<AdapterRow>();
        entity.BindReadPort<IDictionaryCollisionPort>();
        foreach (var policy in new[] { RepositoryDuplicateKeyBehavior.KeepFirst, RepositoryDuplicateKeyBehavior.KeepLast })
        {
            entity.HasQuery(policy == RepositoryDuplicateKeyBehavior.KeepFirst ? "FirstScopedRows" : "LastScopedRows").Results<AdapterRow>().OrderBy(row => row.Id)
                .Dictionary(row => id.ToContract(row.Scope), row => new AdapterRowDto(id.ToContract(row.Id), row.Name, row.Scope), policy);
        }

        var model = TypeAdapterTests.Model(context, builder);
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var port = (IDictionaryCollisionPort)Activator.CreateInstance(TypeAdapterTests.TypeOf(assembly, "AdapterRowRepositoryOperations"), context)!;
        (await port.FirstScopedRowsAsync())[new ApplicationRowId(7)].Id.Should().Be(new ApplicationRowId(1));
        (await port.LastScopedRowsAsync())[new ApplicationRowId(7)].Id.Should().Be(new ApplicationRowId(2));
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public void CollisionPoliciesDoNotLeakDesignerOrEfDependenciesToCompiledConsumers()
    {
        using var context = MetadataContext();
        var builder = Builder();
        builder.Entity<CommandRow>().HasQuery("Read").Results<CommandRow>().OrderBy(row => row.Id)
            .Dictionary(row => row.OwnerId, row => row.Name, RepositoryDuplicateKeyBehavior.KeepFirst);
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(Model(context, builder));
        contracts.Should().NotContain("DuplicateKeyBehavior").And.NotContain("EntityFrameworkCore").And.NotContain("IQueryable");
        const string consumer = "class Consumer { public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyDictionary<int, string>> Read(Dictionary.Contracts.IReadCommandRowRepository repository) => repository.ReadAsync(); }";
        var compilation = CSharpCompilation.Create("IndependentDictionaryContracts", [CSharpSyntaxTree.ParseText(contracts), CSharpSyntaxTree.ParseText(consumer)],
            ClosedRepositoryTests.PlatformReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Should().BeEmpty();
    }

    private static RepositoryModelBuilder Builder(string surface = "closed")
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        if (surface == "closed")
        {
            builder.Configure(options => options.UseClosedRepositories("Dictionary.Contracts").UseCommandTimeout(3));
        }

        if (surface == "cqrs")
        {
            builder.Configure(options => options.UseCqrsSelectors());
        }

        if (surface == "cachedSelectors")
        {
            builder.Configure(options => options.GenerateCacheDecorator());
        }

        return builder;
    }

    private static CommandContext MetadataContext() => new(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
    private static RepositoryGenerationModel Model(CommandContext context, RepositoryModelBuilder builder) => RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, design: builder.Build());
    private static Type TypeOf(Assembly assembly, string name) => assembly.GetType(typeof(CommandRow).Namespace + "." + name, throwOnError: true)!;

    private static object Create(Assembly assembly, CommandContext context, string surface = "closed")
    {
        var repository = Activator.CreateInstance(TypeOf(assembly, surface == "closed" ? "CommandRowRepositoryOperations" : "CommandRowRepository"), context)!;
        return surface == "cqrs" ? Activator.CreateInstance(TypeOf(assembly, "CommandRowReadRepository"), repository)! : repository;
    }

    private static T Invoke<T>(object target, string method, params object[] arguments)
    {
        if (target.GetType().GetMethod(method) is { } instance)
        {
            return (T)instance.Invoke(target, arguments)!;
        }

        return (T)TypeOf(target.GetType().Assembly, "CommandRowConfiguredQueryExtensions").GetMethod(method)!.Invoke(null, new object[] { target }.Concat(arguments).ToArray())!;
    }

    private static void CompileFiles(IEnumerable<string> sources)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(DictionaryCollisionPolicyTests).Assembly.Location).Append(typeof(DbContext).Assembly.Location)
            .Append(typeof(RelationalQueryableExtensions).Assembly.Location).Append(typeof(IDbContextLeaseProvider<>).Assembly.Location)
            .Append(typeof(PagedResult<>).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create("Dictionary_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            paths.Select(path => MetadataReference.CreateFromFile(path)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        result.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error || item.Severity == DiagnosticSeverity.Warning && item.Id != "CS1701").Should().BeEmpty();
        output.Position = 0;
        AssemblyLoadContext.Default.LoadFromStream(output).Should().NotBeNull();
    }

    private sealed class Fixture(SqliteConnection connection, CommandContext context, DbContextOptions<CommandContext> options, Recorder recorder) : IAsyncDisposable
    {
        public CommandContext Context { get; } = context;
        public DbContextOptions<CommandContext> Options { get; } = options;
        public Recorder Sql { get; } = recorder;

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var recorder = new Recorder();
            var options = new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection, sqlite => sqlite.CommandTimeout(17)).AddInterceptors(recorder).Options;
            var context = new CommandContext(options);
            await context.Database.EnsureCreatedAsync();
            context.Owners.AddRange(new CommandOwner { Id = 1 }, new CommandOwner { Id = 2 });
            context.Rows.AddRange(new CommandRow { Id = 1, OwnerId = 1, Name = "Alpha", Description = "first" }, new CommandRow { Id = 2, OwnerId = 1, Name = "Beta", Description = "second" },
                new CommandRow { Id = 3, OwnerId = 1, Name = "Gamma" }, new CommandRow { Id = 4, OwnerId = 2, Name = "Delta", Description = "other" }, new CommandRow { Id = 5, OwnerId = 1, Name = "Hidden" });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            recorder.Commands.Clear();
            return new Fixture(connection, context, options, recorder);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class Recorder : DbCommandInterceptor
    {
        public List<(string Sql, bool Transaction, int Timeout)> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add((command.CommandText, command.Transaction is not null, command.CommandTimeout));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingContext(DbContextOptions<CommandContext> options) : CommandContext(options)
    {
        public int? TimeoutOnDispose { get; private set; }

        public override ValueTask DisposeAsync()
        {
            TimeoutOnDispose = Database.GetCommandTimeout();
            GC.SuppressFinalize(this);
            return base.DisposeAsync();
        }
    }

    private sealed class OwnedProvider(DbContextOptions<CommandContext> options) : IDbContextLeaseProvider<CommandContext>
    {
        public List<RecordingContext> Contexts { get; } = [];

        public ValueTask<DbContextLease<CommandContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new RecordingContext(options);
            Contexts.Add(context);
            return ValueTask.FromResult(DbContextLease.Owned<CommandContext>(context));
        }
    }
}

public sealed record DictionaryPolicyRequest(bool Descending);

public sealed record DictionaryPolicyRow
{
    public int Id { get; init; }
    public int OwnerId { get; init; }
    public string Name { get; init; } = "";
}

public sealed record DictionaryPolicyCount
{
    public int Owner { get; init; }
    public int Count { get; init; }
}

public sealed class DictionaryQueryRoot : IRepositoryQueryRoot<CommandContext, DictionaryPolicyRequest, DictionaryPolicyRow>
{
    public bool SawTransaction { get; private set; }

    public IQueryable<DictionaryPolicyRow> CreateQuery(CommandContext context, DictionaryPolicyRequest request)
    {
        SawTransaction = context.Database.CurrentTransaction is not null;
        return context.Rows.AsNoTracking().OrderBy(row => row.Id).Select(row => new DictionaryPolicyRow { Id = row.Id, OwnerId = row.OwnerId, Name = row.Name });
    }
}

public sealed record DictionaryCancelKey(int Value)
{
    private static CancellationTokenSource? cancellation;
    private static int cancelAtHash = 1;
    internal static int HashCount { get; private set; }

    internal static void CancelOnHash(CancellationTokenSource? source, int hashNumber = 1)
    {
        cancellation = source;
        HashCount = 0;
        cancelAtHash = hashNumber;
    }

    public override int GetHashCode()
    {
        HashCount++;
        if (HashCount == cancelAtHash)
        {
            cancellation?.Cancel();
        }

        return Value.GetHashCode();
    }
}

public static class DictionaryPolicyKeys
{
    // Intentionally malformed infrastructure data exercises runtime key validation, not nullable suppression in expression trees.
    public static string MalformedKey(string? value) => value!;
}

public interface IDictionaryCollisionPort
{
    Task<IReadOnlyDictionary<ApplicationRowId, AdapterRowDto>> FirstScopedRowsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<ApplicationRowId, AdapterRowDto>> LastScopedRowsAsync(CancellationToken cancellationToken = default);
}