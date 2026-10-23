using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
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

public sealed class ResultFieldSortingTests
{
    [Theory]
    [InlineData("field")]
    [InlineData("descending")]
    [InlineData("configure")]
    public void NullArgumentsDoNotAddAStage(string argument)
    {
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Results<PipelineRow>();
        Expression<Func<FieldSortRequest, string?>> field = argument == "field" ? null! : request => request.Field;
        Expression<Func<FieldSortRequest, bool>> descending = argument == "descending" ? null! : request => request.Descending;
        Action<RepositorySortFieldsBuilder<PipelineRow>> configure = argument == "configure" ? null! : fields => fields.Field("id", row => row.Id);
        Action add = () => query.OrderByField(field, descending, configure);
        add.Should().Throw<ArgumentNullException>();
        builder.Build().Entities.Single().Queries.Single().ResultStages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("blank", "RGD2601")]
    [InlineData("duplicate", "RGD2601")]
    [InlineData("empty", "RGD2601")]
    [InlineData("default", "RGD2602")]
    [InlineData("blankDefault", "RGD2602")]
    [InlineData("defaultThenPreserve", "RGD2602")]
    [InlineData("preserveThenDefault", "RGD2602")]
    public void InvalidWhitelistsFailWithoutMutatingThePipeline(string scenario, string diagnostic)
    {
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Results<PipelineRow>();
        Action add = () => query.OrderByField((FieldSortRequest request) => request.Field, request => request.Descending, fields =>
        {
            if (scenario == "empty")
            {
                return;
            }

            fields.Field(scenario == "blank" ? " " : "id", row => row.Id);
            if (scenario == "duplicate")
            {
                fields.Field("ID", row => row.Amount);
            }

            if (scenario == "default")
            {
                fields.UseDefault("missing");
            }

            if (scenario == "blankDefault")
            {
                fields.UseDefault(" ");
            }

            if (scenario == "defaultThenPreserve")
            {
                fields.UseDefault("id").PreserveOrderWhenUnspecified();
            }

            if (scenario == "preserveThenDefault")
            {
                fields.PreserveOrderWhenUnspecified().UseDefault("id");
            }
        });
        add.Should().Throw<Exception>().WithMessage("*" + diagnostic + "*");
        builder.Build().Entities.Single().Queries.Single().ResultStages.Should().BeEmpty();
    }

    [Fact]
    public void NullKeysCallbackFailuresAndStaleBuildersCannotAddSorting()
    {
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Results<PipelineRow>();
        Action nullKey = () => query.OrderByField((FieldSortRequest request) => request.Field, request => request.Descending,
            fields => fields.Field<int>("id", null!));
        nullKey.Should().Throw<ArgumentNullException>();
        Action callback = () => query.OrderByField((FieldSortRequest request) => request.Field, request => request.Descending,
            fields =>
            {
                fields.Field("id", row => row.Id);
                throw new InvalidOperationException("callback");
            });
        callback.Should().Throw<InvalidOperationException>().WithMessage("callback");
        builder.Build().Entities.Single().Queries.Single().ResultStages.Should().BeEmpty();
        query.Select(row => row.Name).List();
        var invoked = false;
        Action stale = () => query.OrderByField((FieldSortRequest request) => request.Field, request => request.Descending, fields =>
        {
            invoked = true;
            fields.Field("id", row => row.Id);
        });
        stale.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        invoked.Should().BeFalse();
    }

    [Fact]
    public void WhitelistsAreCapturedDeterministicallyAndRequestAliasesAreRebound()
    {
        using var context = MetadataContext();
        var builder = Builder();
        RepositorySortFieldsBuilder<PipelineRow>? retained = null;
        builder.Entity<PipelineRow>().HasQuery("Read").Results<PipelineRow>()
            .OrderByField((FieldSortRequest request) => request.Field, preferences => preferences.Descending, fields =>
            {
                retained = fields;
                fields.Field("name", row => row.Name).Field("id", row => row.Id).UseDefault("ID");
            }).Select(row => row.Name).List();
        var model = Model(context, builder);
        var query = model.Entities.Single().Queries.Single();
        query.Parameters.Should().ContainSingle().Which.Name.Should().Be("request");
        var design = builder.Build().Entities.Single().Queries.Single().ResultStages[0].FieldSort!;
        design.FieldName.Parameters[0].Should().BeSameAs(design.Descending.Parameters[0]);
        query.Pipeline!.Stages[0].FieldSort!.DefaultField.Should().Be("id");
        retained!.Field("amount", row => row.Amount).UseDefault("name", true);
        JsonSerializer.Serialize(Model(context, builder)).Should().Be(JsonSerializer.Serialize(model));
        var other = Builder();
        other.Entity<PipelineRow>().HasQuery("Read").Results<PipelineRow>()
            .OrderByField((FieldSortRequest request) => request.Field, request => request.Descending, fields => fields.Field("id", row => row.Id).Field("name", row => row.Name).UseDefault("id"))
            .Select(row => row.Name).List();
        RepositoryGenerator.Generate(model).Files.Should().Equal(RepositoryGenerator.Generate(Model(context, other)).Files);
        var manifest = JsonNode.Parse(RepositoryManifestRenderer.Render(model))!["entities"]![0]!["queries"]![0]!;
        manifest["resultFieldSorting"]![0]!["fields"]!.AsArray().Select(row => row!["alias"]!.GetValue<string>()).Should().Equal("id", "name");
        manifest["resultFieldSorting"]![0]!["unspecified"]!.GetValue<string>().Should().Be("defaultField");
    }

    [Theory]
    [InlineData("then")]
    [InlineData("page")]
    [InlineData("dictionary")]
    [InlineData("distinct")]
    [InlineData("group")]
    public void OptionalFieldSortingCannotPretendToGuaranteeOrder(string terminal)
    {
        using var context = MetadataContext();
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Bad").Select(row => row.Id);
        if (terminal is "distinct" or "group")
        {
            query.OrderBy(id => id);
        }

        if (terminal == "distinct")
        {
            query.Distinct();
        }

        if (terminal == "group")
        {
            query = query.GroupBy(id => id, group => group.Key);
        }

        query.OrderByField((FieldSortRequest request) => request.Field, request => request.Descending, fields => fields.Field("id", id => id).PreserveOrderWhenUnspecified());
        if (terminal == "then")
        {
            query.ThenBy(id => id).List();
        }
        else if (terminal == "dictionary")
        {
            query.Dictionary(id => id, RepositoryDuplicateKeyBehavior.KeepFirst);
        }
        else
        {
            query.RequirePagingOrder().Paged();
        }

        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage(terminal == "then" ? "*RGD2401*" : terminal == "dictionary" ? "*RGD2502*" : "*RGD1501*");
    }

    [Theory]
    [InlineData("strict")]
    [InlineData("default")]
    [InlineData("fallback")]
    public void StrictDefaultAndFallbackFieldSortsAllowAdjacentTiesAndGuardedPaging(string mode)
    {
        using var context = MetadataContext();
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id);
        if (mode == "fallback")
        {
            query.OrderBy(id => id);
        }

        query.OrderByField((FieldSortRequest request) => request.Field, request => request.Descending, fields =>
            {
                fields.Field("id", id => id);
                if (mode == "default")
                {
                    fields.UseDefault("id");
                }

                if (mode == "fallback")
                {
                    fields.PreserveOrderWhenUnspecified();
                }
            })
            .ThenBy(id => id).RequirePagingOrder().Paged();
        Model(context, builder).Entities.Single().Queries.Single().Pipeline!.Stages.Should().Contain(stage => stage.Kind == RepositoryQueryStageKind.OrderByField);
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
    public void AllSupportedSurfacesAndLayoutsCompileStrictly(string surface, RepositoryOutputLayout layout)
    {
        using var context = MetadataContext();
        var builder = Builder(surface);
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("Read").Projection().OrderBy(row => row.Id).OrderByField((FieldSortRequest request) => request.Field, direction => direction.Descending,
                fields => fields.Field("display name", row => row.Name).Field("amount", row => row.Amount).Field("name length", row => row.Name.Length).PreserveOrderWhenUnspecified())
            .ThenBy(row => row.Id).List();
        entity.HasQuery("Nullable").Select(row => (int?)row.Amount).OrderByField((FieldSortRequest request) => request.Field, request => request.Descending,
            fields => fields.Field("value", value => value).UseDefault("value", true)).List();
        entity.HasQuery("Anonymous").Select(row => new { row.Id, row.Name }).OrderByField((FieldSortRequest request) => request.Field, request => request.Descending,
            fields => fields.Field("id", row => row.Id).Field("name", row => row.Name)).ThenBy(row => row.Id).Select(row => row.Name).List();
        if (surface == "cachedSelectors")
        {
            entity.HasQuery("Cached").Cache().Select(row => row.Id).OrderByField((FieldSortRequest request) => request.Field, request => request.Descending, fields => fields.Field("id", id => id)).List();
        }

        var model = Model(context, builder);
        var files = RepositoryGenerator.Generate(model, layout).Files;
        files.Should().Equal(RepositoryGenerator.Generate(model, layout).Files);
        CompileFiles(files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts or RepositoryOutputKind.Projections).Select(file => file.Contents));
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("cqrs")]
    [InlineData("selectors")]
    public async Task AliasesDirectionsAndFallbackOrderExecuteServerSideOnEverySurface(string surface)
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder(surface);
        var entity = builder.Entity<PipelineRow>();
        entity.HasVisibilityPolicy("Visible", row => row.Id < 6);
        entity.ProjectionEnabled = false;
        entity.HasQuery("Read").Projection().OrderBy(row => row.Id).OrderByField((FieldSortRequest request) => request.Field, request => request.Descending,
            fields => fields.Field("display name", row => row.Name).Field("amount", row => row.Amount).PreserveOrderWhenUnspecified()).ThenBy(row => row.Id).Select(row => row.Id).List();
        var target = Create(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), fixture.Context, surface);
        (await Invoke<Task<IReadOnlyList<int>>>(target, "ReadAsync", new FieldSortRequest(null), CancellationToken.None)).Should().Equal(1, 2, 3, 4, 5);
        fixture.Sql.Commands.Single().Sql.Should().NotContain("Amount").And.NotContain("Name");
        fixture.Sql.Commands.Clear();
        (await Invoke<Task<IReadOnlyList<int>>>(target, "ReadAsync", new FieldSortRequest("DISPLAY NAME"), CancellationToken.None)).Should().Equal(3, 2, 5, 4, 1);
        fixture.Sql.Commands.Single().Sql.Should().Contain("Name").And.NotContain("Amount");
        fixture.Sql.Commands.Clear();
        (await Invoke<Task<IReadOnlyList<int>>>(target, "ReadAsync", new FieldSortRequest("display name", true), CancellationToken.None)).Should().Equal(1, 4, 5, 2, 3);
        fixture.Sql.Commands.Single().Sql.Should().Contain("DESC");
        fixture.Sql.Commands.Clear();
        (await Invoke<Task<IReadOnlyList<int>>>(target, "ReadAsync", new FieldSortRequest("amount"), CancellationToken.None)).Should().Equal(2, 1, 3, 4, 5);
        fixture.Sql.Commands.Single().Sql.Should().Contain("Amount").And.NotContain("Name");
        fixture.Sql.Commands.Clear();
        (await Invoke<Task<IReadOnlyList<int>>>(target, "ReadAsync", new FieldSortRequest(""), CancellationToken.None)).Should().Equal(1, 2, 3, 4, 5);
        if (surface == "closed")
        {
            fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Name")]
    [InlineData("name; DROP TABLE Rows")]
    public async Task InvalidOrMissingFieldsAreRejectedBeforeOwnedAcquisition(string? field)
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases());
        builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id).OrderByField((FieldSortRequest request) => request.Field, request => request.Descending, fields => fields.Field("id", id => id)).List();
        var provider = new OwnedProvider(fixture.Options);
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "PipelineRowRepositoryOperations"), provider)!;
        Func<Task> execute = async () => await Invoke<Task<IReadOnlyList<int>>>(operations, "ReadAsync", new FieldSortRequest(field), CancellationToken.None);
        await execute.Should().ThrowAsync<ArgumentException>();
        provider.Contexts.Should().BeEmpty();
        fixture.Sql.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task SortInputsAreEvaluatedOnceAcrossCountAndItemsAndUnspecifiedDirectionsAreNotEvaluated()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("Page").Select(row => row.Id).OrderByField((FieldSortProbeRequest request) => request.Field, direction => direction.Descending,
            fields => fields.Field("id", id => id).UseDefault("ID", true)).RequirePagingOrder().Paged();
        var operations = Create(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), fixture.Context);
        var selected = new FieldSortProbeRequest("id", false);
        (await Invoke<Task<PagedResult<int>>>(operations, "PageAsync", selected, 1, 2, CancellationToken.None)).Items.Should().Equal(1, 2);
        selected.FieldReads.Should().Be(1);
        selected.DirectionReads.Should().Be(1);
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands.Clear();
        var fallback = new FieldSortProbeRequest(null, false) { FailDirection = true };
        (await Invoke<Task<PagedResult<int>>>(operations, "PageAsync", fallback, 1, 2, CancellationToken.None)).Items.Should().Equal(6, 5);
        fallback.FieldReads.Should().Be(1);
        fallback.DirectionReads.Should().Be(0);
        fixture.Context.Database.GetCommandTimeout().Should().Be(17);
        var invalid = new FieldSortProbeRequest("missing", false) { FailDirection = true };
        Func<Task> fail = async () => await Invoke<Task<PagedResult<int>>>(operations, "PageAsync", invalid, 1, 2, CancellationToken.None);
        await fail.Should().ThrowAsync<ArgumentException>();
        invalid.DirectionReads.Should().Be(0);
        fixture.Sql.Commands.Should().HaveCount(2);
    }

    [Fact]
    public async Task GroupedJoinedDistinctPagedAndDictionaryPipelinesRemainProviderComposed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        entity.HasVisibilityPolicy("Visible", row => row.Id < 6);
        entity.HasQuery("Groups").Results<PipelineRow>().GroupBy(row => row.Category, group => new { Category = group.Key, Total = group.Sum(row => row.Amount), Count = group.Count() })
            .OrderByField((FieldSortRequest request) => request.Field, request => request.Descending, fields => fields.Field("total", row => row.Total).Field("count", row => row.Count).UseDefault("count", true))
            .ThenBy(row => row.Category).Select(row => row.Category).RequirePagingOrder().Paged(RepositoryPageConsistency.Serializable);
        entity.HasQuery("Join").From(sources => from left in sources.Set<PipelineRow>() join right in sources.Set<PipelineRow>() on left.Id equals right.Id select new { left.Id, right.Name })
            .OrderByField((FieldSortRequest request) => request.Field, request => request.Descending, fields => fields.Field("name", row => row.Name)).ThenBy(row => row.Id).Select(row => row.Id).Slice();
        entity.HasQuery("Distinct").Select(row => row.Category).Distinct().OrderByField((FieldSortRequest request) => request.Field, request => request.Descending, fields => fields.Field("category", row => row)).List();
        entity.HasQuery("Dictionary").Results<PipelineRow>().OrderByField((FieldSortRequest request) => request.Field, request => request.Descending, fields => fields.Field("name", row => row.Name))
            .ThenBy(row => row.Id).Dictionary(row => row.Category, row => row.Id, RepositoryDuplicateKeyBehavior.KeepFirst);
        var operations = Create(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), fixture.Context);
        var groups = await Invoke<Task<PagedResult<string>>>(operations, "GroupsAsync", new FieldSortRequest("count", true), 1, 10, CancellationToken.None);
        groups.TotalCount.Should().Be(2);
        groups.Items.Should().Equal("A", "B");
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands.Should().OnlyContain(command => command.Transaction);
        fixture.Sql.Commands[^1].Sql.Should().Contain("GROUP BY").And.Contain("COUNT(").And.Contain("DESC");
        fixture.Sql.Commands.Clear();
        (await Invoke<Task<SliceResult<int>>>(operations, "JoinAsync", new FieldSortRequest("name"), 1, 2, CancellationToken.None)).Items.Should().Equal(3, 2);
        fixture.Sql.Commands.Single().Sql.Should().Contain("INNER JOIN");
        fixture.Sql.Commands.Clear();
        (await Invoke<Task<IReadOnlyList<string>>>(operations, "DistinctAsync", new FieldSortRequest("category", true), CancellationToken.None)).Should().Equal("B", "A");
        fixture.Sql.Commands.Single().Sql.Should().Contain("DISTINCT");
        fixture.Sql.Commands.Clear();
        var dictionary = await Invoke<Task<IReadOnlyDictionary<string, int>>>(operations, "DictionaryAsync", new FieldSortRequest("name"), CancellationToken.None);
        dictionary["A"].Should().Be(3);
        dictionary["B"].Should().Be(2);
        fixture.Sql.Commands.Single().Sql.Should().NotContain("Amount");
        fixture.Context.Database.GetCommandTimeout().Should().Be(17);
        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task StreamsPrepareLazilyAndDisposeOwnedLeasesOnEarlyExitAndCancellation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases());
        builder.Entity<PipelineRow>().HasQuery("Stream").Select(row => row.Id).OrderByField((FieldSortProbeRequest request) => request.Field, request => request.Descending, fields => fields.Field("id", id => id)).Stream();
        var provider = new OwnedProvider(fixture.Options);
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "PipelineRowRepositoryOperations"), provider)!;
        var request = new FieldSortProbeRequest("id", true);
        var stream = Invoke<IAsyncEnumerable<int>>(operations, "StreamAsync", request, CancellationToken.None);
        request.FieldReads.Should().Be(0);
        provider.Contexts.Should().BeEmpty();
        var enumerator = stream.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Should().Be(6);
        request.FieldReads.Should().Be(1);
        await enumerator.DisposeAsync();
        provider.Contexts.Single().DisposeCount.Should().Be(1);
        using var cancellation = new CancellationTokenSource();
        var canceled = Invoke<IAsyncEnumerable<int>>(operations, "StreamAsync", new FieldSortProbeRequest("id", false), cancellation.Token).GetAsyncEnumerator();
        (await canceled.MoveNextAsync()).Should().BeTrue();
        cancellation.Cancel();
        Func<Task> move = async () => await canceled.MoveNextAsync();
        await move.Should().ThrowAsync<OperationCanceledException>();
        await canceled.DisposeAsync();
        provider.Contexts.Should().HaveCount(2).And.OnlyContain(context => context.DisposeCount == 1);
        using var earlyCancellation = new CancellationTokenSource();
        var beforeAcquire = Invoke<IAsyncEnumerable<int>>(operations, "StreamAsync", new FieldSortProbeRequest("id", false) { CancelOnField = earlyCancellation }, earlyCancellation.Token).GetAsyncEnumerator();
        Func<Task> start = async () => await beforeAcquire.MoveNextAsync();
        await start.Should().ThrowAsync<OperationCanceledException>();
        await beforeAcquire.DisposeAsync();
        provider.Contexts.Should().HaveCount(2);
    }

    [Fact]
    public async Task PreparedFieldsAndDirectionsDistinguishCacheEntriesEvenForIdenticalSerializedRequests()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryRepositoryResultCache(memory);
        var builder = Builder();
        builder.Configure(options => options.UseClosedCaching("field-sort-fixture"));
        builder.Entity<PipelineRow>().HasQuery("Read").Cache().Results<PipelineRow>().OrderByField((FieldSortProbeRequest request) => request.Field, request => request.Descending,
            fields => fields.Field("id", row => row.Id).Field("name", row => row.Name)).ThenBy(row => row.Id).Select(row => row.Id).List();
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "PipelineRowRepositoryOperations"), fixture.Context, cache)!;
        var ids = new FieldSortProbeRequest("id", false);
        var reverse = new FieldSortProbeRequest("id", true);
        var names = new FieldSortProbeRequest("name", false);
        JsonSerializer.Serialize(ids).Should().Be(JsonSerializer.Serialize(reverse)).And.Be(JsonSerializer.Serialize(names));
        (await Invoke<Task<IReadOnlyList<int>>>(operations, "ReadAsync", ids, CancellationToken.None)).Should().Equal(1, 2, 3, 4, 5, 6);
        (await Invoke<Task<IReadOnlyList<int>>>(operations, "ReadAsync", reverse, CancellationToken.None)).Should().Equal(6, 5, 4, 3, 2, 1);
        var result = await Invoke<Task<IReadOnlyList<int>>>(operations, "ReadAsync", names, CancellationToken.None);
        result.Should().Equal(3, 2, 5, 4, 6, 1);
        ((List<int>)result)[0] = -1;
        fixture.Sql.Commands.Should().HaveCount(3);
        fixture.Sql.Commands.Clear();
        (await Invoke<Task<IReadOnlyList<int>>>(operations, "ReadAsync", new FieldSortProbeRequest("NAME", false), CancellationToken.None)).Should().Equal(3, 2, 5, 4, 6, 1);
        fixture.Sql.Commands.Should().BeEmpty();
        Func<Task> invalid = async () => await Invoke<Task<IReadOnlyList<int>>>(operations, "ReadAsync", new FieldSortProbeRequest("invalid", false), CancellationToken.None);
        await invalid.Should().ThrowAsync<ArgumentException>();
        fixture.Sql.Commands.Should().BeEmpty();
    }

    [Fact]
    public void BoundContractsDoNotLeakSortBuildersAndUnusedConfigurationLeavesOutputUnchanged()
    {
        using var context = MetadataContext();
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        entity.BindReadPort<IFieldSortPort>();
        var query = entity.HasQuery("Read").Select(row => row.Id).OrderBy<int, FieldSortRequest>((id, request) => id).List();
        var original = Model(context, builder);
        var unused = new RepositorySortFieldsBuilder<int>();
        unused.Field("id", id => id).UseDefault("id");
        RepositoryGenerator.Generate(Model(context, builder)).Files.Should().Equal(RepositoryGenerator.Generate(original).Files);
        query.OrderByField((FieldSortRequest request) => request.Field, request => request.Descending, fields => fields.Field("id", id => id).PreserveOrderWhenUnspecified()).List();
        var model = Model(context, builder);
        RepositoryApiSnapshot.Capture(model).ToJson().Should().Be(RepositoryApiSnapshot.Capture(original).ToJson());
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(model);
        contracts.Should().NotContain("OrderByField").And.NotContain("SortFields").And.NotContain("EntityFrameworkCore").And.NotContain("IQueryable");
        const string request = "namespace RonSijm.RepoGen.Tool.Tests { public sealed record FieldSortRequest(string Field, bool Descending); public interface IFieldSortPort { System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<int>> ReadAsync(FieldSortRequest request, System.Threading.CancellationToken cancellationToken = default); } }";
        const string consumer = "class Consumer { public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<int>> Read(FieldSort.Contracts.IReadPipelineRowRepository repository, RonSijm.RepoGen.Tool.Tests.FieldSortRequest request) => repository.ReadAsync(request); }";
        var compilation = CSharpCompilation.Create("IndependentFieldSortingContracts", [CSharpSyntaxTree.ParseText(contracts), CSharpSyntaxTree.ParseText(request), CSharpSyntaxTree.ParseText(consumer)],
            ClosedRepositoryTests.PlatformReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(item => item.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Theory]
    [InlineData("field")]
    [InlineData("direction")]
    [InlineData("key")]
    public void CapturedDesignerStateIsRejectedButExcludedQueriesAreNotRendered(string expression)
    {
        using var context = MetadataContext();
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        var alias = "id";
        var descending = true;
        var bias = 7;
        Expression<Func<FieldSortRequest, string?>> field = expression == "field" ? request => alias : request => request.Field;
        Expression<Func<FieldSortRequest, bool>> direction = expression == "direction" ? request => descending : request => request.Descending;
        Expression<Func<PipelineRow, int>> key = expression == "key" ? row => row.Id + bias : row => row.Id;
        entity.HasQuery("Bad").Results<PipelineRow>().OrderByField(field, direction, fields => fields.Field("id", key)).Select(row => row.Id).List();
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*captures configuration-time state*");
        entity.ExcludeQuery("Bad");
        Model(context, builder).Entities.Single().Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task AliasMatchingAndEscapingAreCultureIndependentAndNeverBecomeSqlIdentifiers()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id)
            .OrderByField((FieldSortRequest request) => request.Field, request => request.Descending,
                fields => fields.Field("id", id => id).Field("\"quoted\"; DROP TABLE Rows", id => id).Field("İ", id => id).UseDefault("id")).List();
        var operations = Create(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), fixture.Context);
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            foreach (var field in new[] { "ID", "\"quoted\"; DROP TABLE Rows", "İ" })
            {
                (await Invoke<Task<IReadOnlyList<int>>>(operations, "ReadAsync", new FieldSortRequest(field, true), CancellationToken.None)).Should().Equal(6, 5, 4, 3, 2, 1);
            }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }

        fixture.Sql.Commands.Should().HaveCount(3).And.OnlyContain(command => !command.Sql.Contains("DROP", StringComparison.Ordinal) && !command.Sql.Contains("quoted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TypeAdaptersExpandInRequestSelectorsAndWhitelistedKeys()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        var alias = builder.HasTypeAdapter<string, FieldSortAlias>(value => new FieldSortAlias(value), value => value.Value);
        var normalized = builder.HasTypeAdapter<string, string>(value => value.Trim(), value => value.Trim());
        builder.Entity<PipelineRow>().HasQuery("Read").Results<PipelineRow>()
            .OrderByField((FieldSortAdaptedRequest request) => alias.ToModel(request.Field), preferences => preferences.Descending,
                fields => fields.Field("name", row => normalized.ToContract(row.Name))).ThenBy(row => row.Id).Select(row => row.Id).List();
        var model = Model(fixture.Context, builder);
        RepositorySourceRenderer.Render(model).Should().NotContain("ToModel(").And.NotContain("ToContract(");
        var operations = Create(ClosedRepositoryTests.Compile(model, rejectWarnings: true), fixture.Context);
        (await Invoke<Task<IReadOnlyList<int>>>(operations, "ReadAsync", new FieldSortAdaptedRequest(new FieldSortAlias("NAME"), false), CancellationToken.None)).Should().Equal(3, 2, 5, 4, 6, 1);
        fixture.Sql.Commands.Single().Sql.Should().Contain("trim(");
    }

    [Fact]
    public async Task MembershipAndFieldSortingSharePreparationAndCountedQueryInputs()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Entity<PipelineRow>().HasQuery("Page").WhereIn(row => row.Id, (FieldSortMembershipRequest request) => request.Ids, options => options.Distinct())
            .Select(row => row.Id).OrderByField((FieldSortMembershipRequest request) => request.Field, direction => direction.Descending, fields => fields.Field("id", id => id)).RequirePagingOrder().Paged();
        var model = Model(fixture.Context, builder);
        model.Entities.Single().Queries.Single().Parameters.Should().ContainSingle();
        var operations = Create(ClosedRepositoryTests.Compile(model, rejectWarnings: true), fixture.Context);
        var page = await Invoke<Task<PagedResult<int>>>(operations, "PageAsync", new FieldSortMembershipRequest("id", true, [1, 2, 2, 3]), 1, 2, CancellationToken.None);
        page.TotalCount.Should().Be(3);
        page.Items.Should().Equal(3, 2);
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands[^1].Sql.Should().Contain("json_each").And.Contain("DESC");
    }

    [Fact]
    public async Task RegisteredRootsAndCommandReadsParticipateInTheSharedTransactionAndTimeoutScope()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases());
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("Registered").FromProvider<FieldSortQueryRoot, FieldSortRequest, ConditionalSummary>(request => request)
            .OrderByField((FieldSortRequest request) => request.Field, request => request.Descending, fields => fields.Field("amount", row => row.Amount))
            .ThenBy(row => row.Id).RequirePagingOrder().Paged();
        entity.HasQuery("LoadForWrite").OnWriteRepository().Results<PipelineRow>().OrderByField((FieldSortRequest request) => request.Field, request => request.Descending,
            fields => fields.Field("amount", row => row.Amount)).ThenBy(row => row.Id).Select(row => row.Id).List();
        await using var work = new RepositoryUnitOfWork<PipelineContext>(fixture.Context);
        var root = new FieldSortQueryRoot();
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "PipelineRowRepositoryOperations"), work, root)!;
        await using var transaction = await work.BeginTransactionAsync();
        var request = new FieldSortRequest("amount", true);
        (await Invoke<Task<PagedResult<ConditionalSummary>>>(operations, "RegisteredAsync", request, 1, 2, CancellationToken.None)).Items.Select(row => row.Id).Should().Equal(6, 5);
        root.SawTransaction.Should().BeTrue();
        root.Timeout.Should().Be(3);
        (await Invoke<Task<IReadOnlyList<int>>>(operations, "LoadForWriteAsync", request, CancellationToken.None)).Should().Equal(6, 5, 1, 3, 4, 2);
        fixture.Sql.Commands.Should().HaveCount(3).And.OnlyContain(command => command.Transaction && command.Timeout == 3);
        await transaction.RollbackAsync();
        fixture.Context.Database.GetCommandTimeout().Should().Be(17);
        fixture.Context.DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task NavigationSortsRequireReviewAndRelatedWritesInvalidateCachedOrdering()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new LoadingContext(new DbContextOptionsBuilder<LoadingContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Orders.AddRange(new LoadingOrder { Id = 1, Customer = new LoadingCustomer { Id = 1, Name = "Z" } },
            new LoadingOrder { Id = 2, Customer = new LoadingCustomer { Id = 2, Name = "A" } });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = NavigationVisibilityTests.Builder();
        builder.Configure(options => options.UseClosedCaching("field-navigation-fixture"));
        var query = builder.Entity<LoadingOrder>().HasQuery("Read").Cache().Results<LoadingOrder>()
            .OrderByField((FieldSortRequest request) => request.Field, request => request.Descending,
                fields => fields.Field("customer", row => row.Customer == null ? "" : row.Customer.Name).Field("id", row => row.Id)).ThenBy(row => row.Id).Select(row => row.Id);
        query.List();
        Action generate = () => RepositoryModelFactory.Create(context, typeof(LoadingOrder).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*RGD2001*");
        query.ReviewNavigationVisibility<LoadingCustomer>("CustomerLive", "This reporting operation deliberately includes all customer names");
        builder.Entity<LoadingCustomer>(entity =>
        {
            entity.RepositoryEnabled = true;
            entity.ReadEnabled = false;
            entity.HasCommand("Rename").Where<int>((customer, id) => customer.Id == id)
                .Update(update => update.SetProperty(customer => customer.Name, (LoadingCustomer customer, string name) => name));
        });
        var model = RepositoryModelFactory.Create(context, typeof(LoadingOrder).Assembly, true, builder.Build());
        model.Entities.Single(entity => entity.EntityName == "LoadingOrder").Queries.Single().Cache.DependentEntityTypeNames.Should().Contain(RepositoryModelFactory.CSharpTypeName(typeof(LoadingCustomer)));
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryRepositoryResultCache(memory);
        var orders = Activator.CreateInstance(assembly.GetType(typeof(LoadingOrder).Namespace + ".LoadingOrderRepositoryOperations")!, context, cache)!;
        var customers = Activator.CreateInstance(assembly.GetType(typeof(LoadingCustomer).Namespace + ".LoadingCustomerRepositoryOperations")!, context, cache)!;
        var request = new FieldSortRequest("customer");
        (await Invoke<Task<IReadOnlyList<int>>>(orders, "ReadAsync", request, CancellationToken.None)).Should().Equal(2, 1);
        (await Invoke<Task<IReadOnlyList<int>>>(orders, "ReadAsync", request, CancellationToken.None)).Should().Equal(2, 1);
        (await Invoke<Task<int>>(customers, "RenameAsync", 2, "ZZ", CancellationToken.None)).Should().Be(1);
        (await Invoke<Task<IReadOnlyList<int>>>(orders, "ReadAsync", request, CancellationToken.None)).Should().Equal(1, 2);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task UnsupportedSelectedKeysFailThroughEfAndDisposeTheOwnedLease()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases());
        builder.Entity<PipelineRow>().HasQuery("Read").Results<PipelineRow>().OrderByField((FieldSortRequest request) => request.Field, request => request.Descending,
            fields => fields.Field("id", row => row.Id).Field("unsupported", row => SqlQueryMapping.CustomMatch(row.Name))).Select(row => row.Id).List();
        var provider = new OwnedProvider(fixture.Options);
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "PipelineRowRepositoryOperations"), provider)!;
        (await Invoke<Task<IReadOnlyList<int>>>(operations, "ReadAsync", new FieldSortRequest("id"), CancellationToken.None)).Should().HaveCount(6);
        fixture.Sql.Commands.Clear();
        Func<Task> read = async () => await Invoke<Task<IReadOnlyList<int>>>(operations, "ReadAsync", new FieldSortRequest("unsupported"), CancellationToken.None);
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*could not be translated*");
        provider.Contexts.Should().HaveCount(2).And.OnlyContain(context => context.DisposeCount == 1);
        fixture.Sql.Commands.Should().BeEmpty();
    }

    [Fact]
    public void SortOnlyRequestsStillCannotExposeAnEfTypeOnAClosedPort()
    {
        using var context = MetadataContext();
        var builder = Builder();
        builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id).OrderByField((FieldSortEfRequest request) => request.Context.Database.ProviderName,
            request => false, fields => fields.Field("id", id => id)).List();
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*RGD1502*Context*");
    }

    [Fact]
    public async Task FixedFieldCursorOrderingAndScalarTerminalsRetainTheirSqlSemantics()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("Cursor").Select(row => row.Id).OrderByField((FieldSortRequest request) => request.Field, request => false,
            fields => fields.Field("id", id => id).UseDefault("id")).CursorPaged<int>((id, cursor) => id > cursor, id => id);
        foreach (var terminal in new[] { "Count", "Exists", "First", "Max" })
        {
            var query = entity.HasQuery(terminal).Select(row => row.Id).OrderByField((FieldSortRequest request) => request.Field, request => request.Descending, fields => fields.Field("id", id => id));
            if (terminal == "Count")
            {
                query.Count();
            }
            else if (terminal == "Exists")
            {
                query.Exists();
            }
            else if (terminal == "First")
            {
                query.FirstOrDefault();
            }
            else
            {
                query.Max(id => id);
            }
        }

        var operations = Create(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), fixture.Context);
        var page = await Invoke<Task<CursorPagedResult<int, int>>>(operations, "CursorAsync", new FieldSortRequest(null, true), new CursorPageRequest<int>(2), CancellationToken.None);
        page.Items.Should().Equal(1, 2);
        page.HasMore.Should().BeTrue();
        fixture.Sql.Commands.Should().ContainSingle();
        fixture.Sql.Commands.Clear();
        (await Invoke<Task<CursorPagedResult<int, int>>>(operations, "CursorAsync", new FieldSortRequest("id", true), new CursorPageRequest<int>(page.NextCursor, 2), CancellationToken.None)).Items.Should().Equal(3, 4);
        fixture.Sql.Commands.Should().ContainSingle();
        fixture.Sql.Commands.Clear();
        var request = new FieldSortRequest("id", true);
        (await Invoke<Task<int>>(operations, "CountAsync", request, CancellationToken.None)).Should().Be(6);
        (await Invoke<Task<bool>>(operations, "ExistsAsync", request, CancellationToken.None)).Should().BeTrue();
        (await Invoke<Task<int?>>(operations, "FirstAsync", request, CancellationToken.None)).Should().Be(6);
        (await Invoke<Task<int>>(operations, "MaxAsync", request, CancellationToken.None)).Should().Be(6);
        fixture.Sql.Commands.Should().HaveCount(4);
    }

    private static RepositoryModelBuilder Builder(string surface = "closed")
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        if (surface == "closed")
        {
            builder.Configure(options => options.UseClosedRepositories("FieldSort.Contracts").UseCommandTimeout(3));
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

    private static PipelineContext MetadataContext() => new(new DbContextOptionsBuilder<PipelineContext>().UseSqlite("Data Source=:memory:").Options);
    private static RepositoryGenerationModel Model(PipelineContext context, RepositoryModelBuilder builder) => RepositoryModelFactory.Create(context, typeof(PipelineRow).Assembly, design: builder.Build());
    private static Type TypeOf(Assembly assembly, string name) => assembly.GetType(typeof(PipelineRow).Namespace + "." + name, throwOnError: true)!;

    private static object Create(Assembly assembly, PipelineContext context, string surface = "closed")
    {
        var repository = Activator.CreateInstance(TypeOf(assembly, surface == "closed" ? "PipelineRowRepositoryOperations" : "PipelineRowRepository"), context)!;
        return surface == "cqrs" ? Activator.CreateInstance(TypeOf(assembly, "PipelineRowReadRepository"), repository)! : repository;
    }

    private static T Invoke<T>(object target, string method, params object[] arguments)
    {
        try
        {
            if (target.GetType().GetMethod(method) is { } instance)
            {
                return (T)instance.Invoke(target, arguments)!;
            }

            var extension = target.GetType().Assembly.GetTypes().Where(type => type.Name == "PipelineRowConfiguredQueryExtensions")
                .SelectMany(type => type.GetMethods()).Single(candidate => candidate.Name == method && candidate.GetParameters().Length == arguments.Length + 1);
            return (T)extension.Invoke(null, new object[] { target }.Concat(arguments).ToArray())!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void CompileFiles(IEnumerable<string> sources)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(ResultFieldSortingTests).Assembly.Location).Append(typeof(DbContext).Assembly.Location)
            .Append(typeof(RelationalQueryableExtensions).Assembly.Location).Append(typeof(IDbContextLeaseProvider<>).Assembly.Location)
            .Append(typeof(PagedResult<>).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create("FieldSorting_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            paths.Select(path => MetadataReference.CreateFromFile(path)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        result.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error || item.Severity == DiagnosticSeverity.Warning && item.Id != "CS1701").Should().BeEmpty();
        output.Position = 0;
        AssemblyLoadContext.Default.LoadFromStream(output).Should().NotBeNull();
    }

    private sealed class Fixture(SqliteConnection connection, PipelineContext context, DbContextOptions<PipelineContext> options, Recorder sql) : IAsyncDisposable
    {
        public PipelineContext Context { get; } = context;
        public DbContextOptions<PipelineContext> Options { get; } = options;
        public Recorder Sql { get; } = sql;

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var sql = new Recorder();
            var options = new DbContextOptionsBuilder<PipelineContext>().UseSqlite(connection, sqlite => sqlite.CommandTimeout(17)).AddInterceptors(sql).Options;
            var context = new PipelineContext(options);
            await context.Database.EnsureCreatedAsync();
            context.Rows.AddRange(new PipelineRow { Id = 1, Category = "A", Amount = 20, Name = "Zulu" }, new PipelineRow { Id = 2, Category = "B", Amount = 10, Name = "Beta" },
                new PipelineRow { Id = 3, Category = "A", Amount = 20, Name = "Alpha" }, new PipelineRow { Id = 4, Category = "B", Amount = 20, Name = "Gamma" },
                new PipelineRow { Id = 5, Category = "A", Amount = 30, Name = "Delta" }, new PipelineRow { Id = 6, Category = "C", Amount = 99, Name = "Hidden" });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            sql.Commands.Clear();
            return new Fixture(connection, context, options, sql);
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

    private sealed class OwnedProvider(DbContextOptions<PipelineContext> options) : IDbContextLeaseProvider<PipelineContext>
    {
        public List<PipelineContext> Contexts { get; } = [];

        public ValueTask<DbContextLease<PipelineContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new PipelineContext(options);
            Contexts.Add(context);
            return ValueTask.FromResult(DbContextLease.Owned(context));
        }
    }
}

internal static class FieldSortTestProjections
{
    internal static RepositoryResultQueryBuilder<ConditionalSummary> Projection(this RepositoryQueryBuilder<PipelineRow> query) =>
        query.Select(row => new ConditionalSummary { Id = row.Id, Name = row.Name, Amount = row.Amount });
}

public sealed record FieldSortRequest(string? Field, bool Descending = false);

public sealed class FieldSortProbeRequest(string? sortAlias, bool descending)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Field
    {
        get
        {
            FieldReads++;
            CancelOnField?.Cancel();
            return sortAlias;
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Descending
    {
        get
        {
            DirectionReads++;
            if (FailDirection)
            {
                throw new InvalidOperationException("Direction must not be evaluated.");
            }

            return descending;
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public int FieldReads { get; private set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int DirectionReads { get; private set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool FailDirection { get; init; }

    internal CancellationTokenSource? CancelOnField { get; init; }
}

public interface IFieldSortPort
{
    Task<IReadOnlyList<int>> ReadAsync(FieldSortRequest request, CancellationToken cancellationToken = default);
}

public sealed record FieldSortAlias(string Value);

public sealed record FieldSortAdaptedRequest(FieldSortAlias Field, bool Descending);

public sealed record FieldSortMembershipRequest(string Field, bool Descending, int[] Ids);

public sealed record FieldSortEfRequest(DbContext Context);

public sealed class FieldSortQueryRoot : IRepositoryQueryRoot<PipelineContext, FieldSortRequest, ConditionalSummary>
{
    public bool SawTransaction { get; private set; }
    public int? Timeout { get; private set; }

    public IQueryable<ConditionalSummary> CreateQuery(PipelineContext context, FieldSortRequest request)
    {
        SawTransaction = context.Database.CurrentTransaction is not null;
        Timeout = context.Database.GetCommandTimeout();
        return context.Rows.AsNoTracking().Select(row => new ConditionalSummary { Id = row.Id, Amount = row.Amount, Name = row.Name });
    }
}