using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
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

public sealed class RequestTerminalTests
{
    [Theory]
    [InlineData("dictionaryKey")]
    [InlineData("dictionaryValue")]
    [InlineData("lookupKey")]
    [InlineData("lookupValue")]
    [InlineData("sum")]
    [InlineData("average")]
    [InlineData("max")]
    [InlineData("min")]
    public void NullSelectorsDoNotReplaceAnExistingTerminal(string scenario)
    {
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id).List();
        Action configure = () =>
        {
            switch (scenario)
            {
                case "dictionaryKey": query.Dictionary<int, TerminalRequest>(null!); break;
                case "dictionaryValue": query.Dictionary<int, int, TerminalRequest>(id => id, null!); break;
                case "lookupKey": query.Lookup<int, TerminalRequest>(null!); break;
                case "lookupValue": query.Lookup<int, int, TerminalRequest>(id => id, null!); break;
                case "sum": query.Sum<TerminalRequest>((Expression<Func<int, TerminalRequest, int>>)null!); break;
                case "average": query.Average<TerminalRequest>((Expression<Func<int, TerminalRequest, int>>)null!); break;
                case "max": query.Max<int, TerminalRequest>(null!); break;
                case "min": query.Min<int, TerminalRequest>(null!); break;
            }
        };
        configure.Should().Throw<ArgumentNullException>();
        var design = builder.Build().Entities.Single().Queries.Single();
        design.ResultKind.Should().Be(RepositoryQueryResultKind.List);
        design.Dictionary.Should().BeNull();
        design.Scalar.Should().BeNull();
    }

    [Fact]
    public void InvalidPoliciesAndStaleBuildersCannotMutateTheTerminal()
    {
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id).List();
        Action invalid = () => query.Dictionary<int, TerminalRequest>((id, request) => id + request.Adjustment, (RepositoryDuplicateKeyBehavior)42);
        invalid.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*RGD2501*");
        builder.Build().Entities.Single().Queries.Single().ResultKind.Should().Be(RepositoryQueryResultKind.List);
        var latest = query.Select(id => id + 1).List();
        Action staleDictionary = () => query.Dictionary<int, TerminalRequest>((id, request) => id);
        Action staleSum = () => query.Sum<TerminalRequest>((id, request) => id);
        staleDictionary.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        staleSum.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        latest.Sum<TerminalRequest>((id, request) => id + request.Adjustment).Lookup<int, TerminalRequest>((id, request) => id);
        var design = builder.Build().Entities.Single().Queries.Single();
        design.Scalar.Should().BeNull();
        design.Dictionary.Should().NotBeNull();
        latest.List();
        design.Dictionary.Should().BeNull();
    }

    [Fact]
    public void PairedMappingsShareTheKeysRequestNameAndOnlyTerminalInputsReachTheManifest()
    {
        using var context = MetadataContext();
        var builder = Builder();
        builder.Entity<PipelineRow>().HasQuery("Read").Results<PipelineRow>()
            .Dictionary((PipelineRow row, TerminalRequest request) => row.Id + request.Adjustment,
                (PipelineRow row, TerminalRequest format) => row.Name + format.Suffix);
        var design = builder.Build();
        var terminal = design.Entities.Single().Queries.Single().Dictionary!;
        terminal.ValueSelector.Parameters[1].Should().BeSameAs(terminal.KeySelector.Parameters[1]);
        var model = Model(context, builder);
        var query = model.Entities.Single().Queries.Single();
        query.Parameters.Should().ContainSingle().Which.Name.Should().Be("request");
        query.Dictionary!.ValueBodySource.Should().Contain("request.Suffix").And.NotContain("format");
        query.ApiDataTypes.Should().Contain(typeof(TerminalRequest));
        var inventory = JsonNode.Parse(RepositoryManifestRenderer.Render(model))!["entities"]![0]!["queries"]![0]!["parameters"]!.AsArray();
        inventory.Should().HaveCount(1);
        inventory[0]!["name"]!.GetValue<string>().Should().Be("request");
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
    public void EverySurfaceAndLayoutCompilesAllRequestAwareTerminalShapes(string surface, RepositoryOutputLayout layout)
    {
        using var context = MetadataContext();
        var builder = Builder(surface);
        var entity = builder.Entity<PipelineRow>();
        ConfigureMappings(entity, cache: surface == "cachedSelectors");
        ConfigureNumericTerminals(entity);
        entity.HasQuery("Anonymous").Select(row => new { row.Id, row.Name, row.Category })
            .Dictionary<int, string, TerminalRequest>((row, request) => row.Id + request.Adjustment, (row, request) => row.Name + request.Suffix);
        var model = Model(context, builder);
        var files = RepositoryGenerator.Generate(model, layout).Files;
        files.Should().Equal(RepositoryGenerator.Generate(model, layout).Files);
        CompileFiles(files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts or RepositoryOutputKind.Projections).Select(file => file.Contents));
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("cqrs")]
    [InlineData("selectors")]
    public async Task DictionaryAndLookupRequestMappingsSelectOnlyApprovedColumnsOnAllSurfaces(string surface)
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder(surface);
        var entity = builder.Entity<PipelineRow>();
        entity.HasVisibilityPolicy("Visible", row => row.Id < 6);
        ConfigureMappings(entity);
        var repository = Create(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), fixture.Context, surface);
        var request = new TerminalRequest(100, "!", 0);
        var pairs = await Invoke<Task<IReadOnlyDictionary<int, string>>>(repository, "BothAsync", request, CancellationToken.None);
        pairs.Keys.Should().Equal(101, 102, 103, 104, 105);
        pairs[101].Should().Be("Zulu!");
        fixture.Sql.Commands.Should().ContainSingle().Which.Sql.Should().Contain("Id").And.Contain("Name").And.NotContain("Amount");
        var values = await Invoke<Task<IReadOnlyDictionary<int, string>>>(repository, "ValueAsync", request, CancellationToken.None);
        values[1].Should().Be("Zulu!");
        var keys = await Invoke<Task<IReadOnlyDictionary<int, string>>>(repository, "KeyAsync", request, CancellationToken.None);
        keys[101].Should().Be("Zulu");
        var rows = await Invoke<Task<IReadOnlyDictionary<int, int>>>(repository, "RowsAsync", request, CancellationToken.None);
        rows[101].Should().Be(1);
        var first = await Invoke<Task<IReadOnlyDictionary<string, string>>>(repository, "FirstAsync", request, CancellationToken.None);
        first["A!"].Should().Be("Zulu!");
        var last = await Invoke<Task<IReadOnlyDictionary<string, string>>>(repository, "LastAsync", request, CancellationToken.None);
        last["A!"].Should().Be("Delta!");
        var lookup = await Invoke<Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>>(repository, "LookupAsync", request, CancellationToken.None);
        lookup["A!"].Should().Equal("Zulu!", "Alpha!", "Delta!");
        var lookupKeys = await Invoke<Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>>(repository, "LookupKeyAsync", request, CancellationToken.None);
        lookupKeys["A!"].Should().Equal("Zulu", "Alpha", "Delta");
        var lookupValues = await Invoke<Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>>(repository, "LookupValueAsync", request, CancellationToken.None);
        lookupValues["A"].Should().Equal("Zulu!", "Alpha!", "Delta!");
        var lookupRows = await Invoke<Task<IReadOnlyDictionary<int, IReadOnlyList<int>>>>(repository, "LookupRowsAsync", request, CancellationToken.None);
        lookupRows[101].Should().Equal(1);
        fixture.Sql.Commands.Should().HaveCount(10).And.OnlyContain(command => command.Timeout == 17);
        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
        fixture.Context.DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task ScalarMappingsBindTheSameInputAcrossRootGroupAndAggregateWithoutClientEvaluation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        entity.HasVisibilityPolicy("Visible", row => row.Id < 6);
        entity.HasQuery("Sum").Where<TerminalRequest>((row, request) => row.Amount >= request.Minimum).Select(row => row.Amount)
            .Sum<TerminalRequest>((amount, request) => amount + request.Adjustment);
        entity.HasQuery("Average").Select(row => row.Amount).Average<TerminalRequest>((amount, request) => (double)(amount + request.Adjustment));
        entity.HasQuery("Max").Select(row => row.Amount).Max<int, TerminalRequest>((amount, request) => amount + request.Adjustment);
        entity.HasQuery("Min").Select(row => row.Amount).Min<int, TerminalRequest>((amount, request) => amount + request.Adjustment);
        entity.HasQuery("Grouped").Results<PipelineRow>().GroupBy(row => row.Category, group => new { Category = group.Key, Total = group.Sum(row => row.Amount) })
            .Where<TerminalRequest>((group, request) => group.Total > request.Minimum).Sum<TerminalRequest>((group, request) => group.Total + request.Adjustment);
        var model = Model(fixture.Context, builder);
        model.Entities.Single().Queries.Should().OnlyContain(query => query.Parameters.Count == 1);
        var operations = Create(ClosedRepositoryTests.Compile(model, rejectWarnings: true), fixture.Context);
        var request = new TerminalRequest(3, "", 20);
        (await Invoke<Task<int>>(operations, "SumAsync", request, CancellationToken.None)).Should().Be(102);
        (await Invoke<Task<double>>(operations, "AverageAsync", request, CancellationToken.None)).Should().Be(23);
        (await Invoke<Task<int>>(operations, "MaxAsync", request, CancellationToken.None)).Should().Be(33);
        (await Invoke<Task<int>>(operations, "MinAsync", request, CancellationToken.None)).Should().Be(13);
        (await Invoke<Task<int>>(operations, "GroupedAsync", request, CancellationToken.None)).Should().Be(106);
        fixture.Sql.Commands.Should().HaveCount(5);
        fixture.Sql.Commands[0].Sql.Should().Contain("SUM(").And.NotContain("Name");
        fixture.Sql.Commands[1].Sql.Should().Contain("AVG(");
        fixture.Sql.Commands[2].Sql.Should().Contain("MAX(");
        fixture.Sql.Commands[3].Sql.Should().Contain("MIN(");
        fixture.Sql.Commands[4].Sql.Should().Contain("GROUP BY").And.Contain("SUM(");
        fixture.Sql.Commands.Should().OnlyContain(command => command.Values.Contains(3));
        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData("key")]
    [InlineData("value")]
    [InlineData("scalar")]
    public void TerminalOnlyInputsCannotExposeEfThroughClosedContracts(string scenario)
    {
        using var context = MetadataContext();
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id);
        if (scenario == "key")
        {
            query.Dictionary<int, TerminalEfRequest>((id, request) => id);
        }
        else if (scenario == "value")
        {
            query.Dictionary(id => id, (int id, TerminalEfRequest request) => id);
        }
        else
        {
            query.Sum<TerminalEfRequest>((id, request) => id);
        }

        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1502:*request*");
    }

    [Theory]
    [InlineData("key")]
    [InlineData("value")]
    [InlineData("scalar")]
    public void TerminalMappingsStillRejectCapturedDesignerState(string scenario)
    {
        using var context = MetadataContext();
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id);
        var captured = new TerminalRequest(1, "!", 0);
        if (scenario == "key")
        {
            query.Dictionary<int, TerminalRequest>((id, request) => id + captured.Adjustment);
        }
        else if (scenario == "value")
        {
            query.Dictionary(id => id, (int id, TerminalRequest request) => id + captured.Adjustment);
        }
        else
        {
            query.Sum<TerminalRequest>((id, request) => id + captured.Adjustment);
        }

        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*captures configuration-time state*");
    }

    [Fact]
    public void PairedMappingRebindingProtectsNestedBindersFromRequestCapture()
    {
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id);
        Expression<Func<int, TerminalRequest, int>> key = (id, request) => id;
        var row = Expression.Parameter(typeof(int), "id");
        var input = Expression.Parameter(typeof(TerminalRequest), "format");
        // The nested lambda deliberately reuses the target outer parameter object. It must stay local.
        var nestedRequest = key.Parameters[1];
        var suffix = Expression.Property(nestedRequest, nameof(TerminalRequest.Suffix));
        var nested = Expression.Lambda<Func<TerminalRequest, bool>>(Expression.Equal(suffix, Expression.Constant("inner")), nestedRequest);
        var any = Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), [typeof(TerminalRequest)],
            Expression.NewArrayInit(typeof(TerminalRequest), Expression.New(typeof(TerminalRequest).GetConstructors().Single(), Expression.Constant(0), Expression.Constant("inner"), Expression.Constant(0))), nested);
        var value = Expression.Lambda<Func<int, TerminalRequest, string>>(Expression.Condition(any, Expression.Property(input, nameof(TerminalRequest.Suffix)), Expression.Constant("bad")), row, input);
        query.Dictionary(key, value);
        var rebound = builder.Build().Entities.Single().Queries.Single().Dictionary!.ValueSelector;
        var condition = (ConditionalExpression)rebound.Body;
        var reboundNested = (LambdaExpression)((MethodCallExpression)condition.Test).Arguments[1];
        reboundNested.Parameters[0].Should().NotBeSameAs(rebound.Parameters[1]);
        rebound.Compile().DynamicInvoke(1, new TerminalRequest(0, "outer", 0)).Should().Be("outer");
    }

    [Fact]
    public async Task TerminalOnlyNullableInputsAndValuesAreAnnotatedBeforeSignatureCapture()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Entity<PipelineRow>().HasQuery("Nullable").Results<PipelineRow>().OrderBy(row => row.Id)
            .Dictionary<int, string?, TerminalRequest>((row, request) => request == null ? row.Id : row.Id + request.Adjustment,
                (row, format) => format == null || format.Minimum < 0 ? null : row.Name + format.Suffix)
            .AllowNullInput<TerminalRequest>("request").ResultNullability<string>(type => type.Nullable());
        var model = Model(fixture.Context, builder);
        model.Entities.Single().Queries.Single().Parameters.Single().TypeName.Should().EndWith("?");
        var operations = Create(ClosedRepositoryTests.Compile(model, rejectWarnings: true), fixture.Context);
        var values = await Invoke<Task<IReadOnlyDictionary<int, string?>>>(operations, "NullableAsync", null!, CancellationToken.None);
        values.Keys.Should().Equal(1, 2, 3, 4, 5, 6);
        values.Values.Should().OnlyContain(value => value == null);
        var populated = await Invoke<Task<IReadOnlyDictionary<int, string?>>>(operations, "NullableAsync", new TerminalRequest(10, "!", 0), CancellationToken.None);
        populated[11].Should().Be("Zulu!");
        fixture.Sql.Commands.Should().HaveCount(2);
    }

    [Fact]
    public async Task TerminalMappingsExpandInputAndOutputAdaptersWithoutRuntimeMappingServices()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        var id = builder.HasTypeAdapter<int, TerminalId>(value => new TerminalId(value), value => value.Value);
        builder.Entity<PipelineRow>().HasQuery("Read").Results<PipelineRow>()
            .Dictionary((PipelineRow row, TerminalAdaptedRequest request) => id.ToContract(row.Id + id.ToModel(request.Adjustment)), row => row.Name);
        builder.Entity<PipelineRow>().HasQuery("Sum").Select(row => row.Amount)
            .Sum<TerminalAdaptedRequest>((amount, request) => amount + id.ToModel(request.Adjustment));
        var model = Model(fixture.Context, builder);
        var operations = Create(ClosedRepositoryTests.Compile(model, rejectWarnings: true), fixture.Context);
        var request = new TerminalAdaptedRequest(new TerminalId(10));
        (await Invoke<Task<IReadOnlyDictionary<TerminalId, string>>>(operations, "ReadAsync", request, CancellationToken.None))[new TerminalId(11)].Should().Be("Zulu");
        (await Invoke<Task<int>>(operations, "SumAsync", request, CancellationToken.None)).Should().Be(259);
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands[0].Sql.Should().NotContain("Amount");
        fixture.Sql.Commands[1].Sql.Should().Contain("SUM(");
    }

    [Fact]
    public async Task CacheKeysIncludeTerminalOnlyRequestsAndDictionaryAndLookupValuesAreCloned()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryRepositoryResultCache(memory);
        var builder = Builder();
        builder.Configure(options => options.UseClosedCaching("terminal-fixture"));
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("Read").Cache().Results<PipelineRow>().Dictionary(row => row.Id, (PipelineRow row, TerminalRequest request) => row.Name + request.Suffix);
        entity.HasQuery("Lookup").Cache().Results<PipelineRow>().OrderBy(row => row.Id).Lookup(row => row.Category, (PipelineRow row, TerminalRequest request) => row.Name + request.Suffix);
        entity.HasQuery("Sum").Cache().Select(row => row.Amount).Sum<TerminalRequest>((amount, request) => amount + request.Adjustment);
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "PipelineRowRepositoryOperations"), fixture.Context, cache)!;
        var first = new TerminalRequest(1, "!", 0);
        var second = new TerminalRequest(2, "?", 0);
        var result = await Invoke<Task<IReadOnlyDictionary<int, string>>>(operations, "ReadAsync", first, CancellationToken.None);
        ((Dictionary<int, string>)result)[1] = "mutated";
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(operations, "ReadAsync", first with { }, CancellationToken.None))[1].Should().Be("Zulu!");
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(operations, "ReadAsync", second, CancellationToken.None))[1].Should().Be("Zulu?");
        var groups = await Invoke<Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>>(operations, "LookupAsync", first, CancellationToken.None);
        ((List<string>)groups["A"])[0] = "mutated";
        (await Invoke<Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>>(operations, "LookupAsync", first, CancellationToken.None))["A"][0].Should().Be("Zulu!");
        (await Invoke<Task<int>>(operations, "SumAsync", first, CancellationToken.None)).Should().Be(205);
        (await Invoke<Task<int>>(operations, "SumAsync", second, CancellationToken.None)).Should().Be(211);
        (await Invoke<Task<int>>(operations, "SumAsync", first, CancellationToken.None)).Should().Be(205);
        fixture.Sql.Commands.Should().HaveCount(5);
    }

    [Fact]
    public void BoundPortsCompileIndependentlyAndTheirExistingSignaturesDoNotChange()
    {
        using var context = MetadataContext();
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        entity.BindReadPort<ITerminalPort>();
        var query = entity.HasQuery("Read").Select<int, TerminalRequest>((row, request) => row.Id).Dictionary(id => id);
        var before = Model(context, builder);
        query.Dictionary<int, TerminalRequest>((id, request) => id + request.Adjustment);
        var model = Model(context, builder);
        RepositoryApiSnapshot.Capture(model).ToJson().Should().Be(RepositoryApiSnapshot.Capture(before).ToJson());
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(model);
        contracts.Should().NotContain("EntityFrameworkCore").And.NotContain("IQueryable").And.NotContain("Expression<");
        const string port = "namespace RonSijm.RepoGen.Tool.Tests { public sealed record TerminalRequest(int Adjustment, string Suffix, int Minimum); public interface ITerminalPort { System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyDictionary<int, int>> ReadAsync(TerminalRequest request, System.Threading.CancellationToken cancellationToken = default); } }";
        const string consumer = "class Consumer { public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyDictionary<int, int>> Read(Terminal.Contracts.IReadPipelineRowRepository repository, RonSijm.RepoGen.Tool.Tests.TerminalRequest request) => repository.ReadAsync(request); }";
        var compilation = CSharpCompilation.Create("IndependentTerminalContracts", [CSharpSyntaxTree.ParseText(contracts), CSharpSyntaxTree.ParseText(port), CSharpSyntaxTree.ParseText(consumer)],
            ClosedRepositoryTests.PlatformReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(item => item.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public async Task RegisteredRootsAndCommandSideAggregatesUseTheSharedTransactionAndRestoreTimeouts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases().UseCommandTimeout(3));
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("Registered").FromProvider<TerminalQueryRoot, TerminalRequest, ConditionalSummary>(request => request)
            .Dictionary(row => row.Id, (ConditionalSummary row, TerminalRequest request) => row.Name + request.Suffix);
        entity.HasQuery("ForCommand").OnWriteRepository().Select(row => row.Amount).Sum<TerminalRequest>((amount, request) => amount + request.Adjustment);
        var root = new TerminalQueryRoot();
        await using var work = new RepositoryUnitOfWork<PipelineContext>(new ScopedDbContextLeaseProvider<PipelineContext>(fixture.Context));
        var assembly = ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true);
        var operations = Activator.CreateInstance(TypeOf(assembly, "PipelineRowRepositoryOperations"), work, root)!;
        await using var transaction = await work.BeginTransactionAsync();
        var request = new TerminalRequest(2, "!", 0);
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(operations, "RegisteredAsync", request, CancellationToken.None))[1].Should().Be("Zulu!");
        (await Invoke<Task<int>>(operations, "ForCommandAsync", request, CancellationToken.None)).Should().Be(211);
        root.SawTransaction.Should().BeTrue();
        fixture.Sql.Commands.Should().HaveCount(2).And.OnlyContain(command => command.Transaction && command.Timeout == 3);
        await transaction.RollbackAsync();
        fixture.Context.Database.GetCommandTimeout().Should().Be(17);
        fixture.Context.DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task GuardsAndCancellationRejectBeforeOwnedAcquisitionAndTranslationFailuresDisposeTheLease()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases().UseCommandTimeout(3));
        builder.Entity<PipelineRow>().HasQuery("Read").Validate<TerminalRequest>(request => request.Adjustment >= 0, "Adjustment must be non-negative.")
            .Select(row => row.Amount).Sum<TerminalRequest>((amount, request) => amount + request.Adjustment);
        builder.Entity<PipelineRow>().HasQuery("Unsupported").Select(row => row.Name)
            .Sum<TerminalRequest>((name, request) => SqlQueryMapping.CustomMatch(name) ? request.Adjustment : 0);
        var provider = new OwnedProvider(fixture.Options);
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "PipelineRowRepositoryOperations"), provider)!;
        Func<Task> invalid = async () => await Invoke<Task<int>>(operations, "ReadAsync", new TerminalRequest(-1, "", 0), CancellationToken.None);
        await invalid.Should().ThrowAsync<ArgumentException>();
        provider.Contexts.Should().BeEmpty();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Func<Task> canceled = async () => await Invoke<Task<int>>(operations, "ReadAsync", new TerminalRequest(1, "", 0), cancellation.Token);
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        provider.Contexts.Should().BeEmpty();
        Func<Task> unsupported = async () => await Invoke<Task<int>>(operations, "UnsupportedAsync", new TerminalRequest(1, "", 0), CancellationToken.None);
        await unsupported.Should().ThrowAsync<InvalidOperationException>().WithMessage("*could not be translated*");
        provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
        fixture.Sql.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyAndNullableAggregatesKeepEfCardinalityAndDictionaryCollisionSemantics()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("EmptySum").Where(row => row.Id < 0).Select(row => row.Amount).Sum<TerminalRequest>((amount, request) => amount + request.Adjustment);
        entity.HasQuery("EmptyMin").Where(row => row.Id < 0).Select(row => row.Amount).Min<int?, TerminalRequest>((amount, request) => (int?)(amount + request.Adjustment));
        entity.HasQuery("EmptyAverage").Where(row => row.Id < 0).Select(row => row.Amount).Average<TerminalRequest>((amount, request) => (int?)(amount + request.Adjustment));
        entity.HasQuery("EmptyMax").Where(row => row.Id < 0).Select(row => row.Amount).Max<int, TerminalRequest>((amount, request) => amount + request.Adjustment);
        entity.HasQuery("Duplicate").Select(row => row.Category).Dictionary<string, TerminalRequest>((category, request) => category + request.Suffix);
        var operations = Create(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), fixture.Context);
        var request = new TerminalRequest(1, "!", 0);
        (await Invoke<Task<int>>(operations, "EmptySumAsync", request, CancellationToken.None)).Should().Be(0);
        (await Invoke<Task<int?>>(operations, "EmptyMinAsync", request, CancellationToken.None)).Should().BeNull();
        (await Invoke<Task<double?>>(operations, "EmptyAverageAsync", request, CancellationToken.None)).Should().BeNull();
        Func<Task> max = async () => await Invoke<Task<int>>(operations, "EmptyMaxAsync", request, CancellationToken.None);
        await max.Should().ThrowAsync<InvalidOperationException>();
        Func<Task> duplicate = async () => await Invoke<Task<IReadOnlyDictionary<string, string>>>(operations, "DuplicateAsync", request, CancellationToken.None);
        await duplicate.Should().ThrowAsync<ArgumentException>();
        fixture.Sql.Commands.Should().HaveCount(5);
    }

    [Theory]
    [InlineData(RepositoryDuplicateKeyBehavior.Throw)]
    [InlineData(RepositoryDuplicateKeyBehavior.KeepFirst)]
    [InlineData(RepositoryDuplicateKeyBehavior.KeepLast)]
    public async Task RequestGeneratedNullKeysAreRejectedAndOwnedContextsAreDisposed(RepositoryDuplicateKeyBehavior policy)
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases().UseCommandTimeout(3));
        builder.Entity<PipelineRow>().HasQuery("Read").Results<PipelineRow>().OrderBy(row => row.Id)
            .Dictionary<string, string, TerminalRequest>((row, request) => request.Suffix, (row, format) => row.Name + format.Suffix, policy);
        var provider = new OwnedProvider(fixture.Options);
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "PipelineRowRepositoryOperations"), provider)!;
        Func<Task> execute = async () => await Invoke<Task<IReadOnlyDictionary<string, string>>>(operations, "ReadAsync", new TerminalRequest(0, null!, 0), CancellationToken.None);
        await execute.Should().ThrowAsync<ArgumentNullException>();
        provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
        fixture.Sql.Commands.Should().ContainSingle().Which.Sql.Should().NotContain("Amount");
    }

    [Fact]
    public async Task TerminalNavigationMappingsRequireReviewAndRelatedWritesInvalidateTheirCache()
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
        builder.Configure(options => options.UseClosedCaching("terminal-navigation-fixture"));
        var query = builder.Entity<LoadingOrder>().HasQuery("Read").Cache().Results<LoadingOrder>()
            .Dictionary<int, string, TerminalRequest>((row, request) => row.Id + request.Adjustment,
                (row, formatting) => (row.Customer == null ? "" : row.Customer.Name) + formatting.Suffix);
        Action generate = () => RepositoryModelFactory.Create(context, typeof(LoadingOrder).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*RGD2001*");
        query.ReviewNavigationVisibility<LoadingCustomer>("CustomerLive", "This infrastructure report deliberately includes all customer names");
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
        var request = new TerminalRequest(100, "!", 0);
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(orders, "ReadAsync", request, CancellationToken.None))[102].Should().Be("A!");
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(orders, "ReadAsync", request, CancellationToken.None))[102].Should().Be("A!");
        (await Invoke<Task<int>>(customers, "RenameAsync", 2, "ZZ", CancellationToken.None)).Should().Be(1);
        (await Invoke<Task<IReadOnlyDictionary<int, string>>>(orders, "ReadAsync", request, CancellationToken.None))[102].Should().Be("ZZ!");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public void ExcludedTerminalRequestsDoNotChangeExistingGenerationAndReservedNamesAreRejected()
    {
        using var context = MetadataContext();
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("Read").Count();
        var original = RepositoryGenerator.Generate(Model(context, builder)).Files;
        entity.HasQuery("Unused").Select(row => row.Id).Sum<TerminalEfRequest>((id, request) => id);
        entity.ExcludeQuery("Unused");
        RepositoryGenerator.Generate(Model(context, builder)).Files.Should().Equal(original);
        entity.HasQuery("Bad").Select(row => row.Id).Sum<TerminalRequest>((id, entity) => id);
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*conflicts with generated infrastructure*");
    }

    [Fact]
    public async Task MembershipAndFieldSortingShareTheTerminalRequestWithoutExtraInputsOrClientSorting()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Entity<PipelineRow>().HasQuery("Read")
            .WhereIn(row => row.Id, (TerminalCompositeRequest request) => request.Ids, options => options.Distinct())
            .Results<PipelineRow>().OrderByField((TerminalCompositeRequest request) => request.SortBy, direction => false,
                fields => fields.Field("name", row => row.Name).Field("amount", row => row.Amount).UseDefault("amount"))
            .ThenBy(row => row.Id).Dictionary<int, string, TerminalCompositeRequest>((row, request) => row.Id + request.Adjustment,
                (row, formatting) => row.Name + formatting.Suffix);
        var model = Model(fixture.Context, builder);
        model.Entities.Single().Queries.Single().Parameters.Should().ContainSingle();
        var operations = Create(ClosedRepositoryTests.Compile(model, rejectWarnings: true), fixture.Context);
        var ids = new[] { 3, 1, 3 };
        var request = new TerminalCompositeRequest(ids, null, 7, "!");
        var first = await Invoke<Task<IReadOnlyDictionary<int, string>>>(operations, "ReadAsync", request, CancellationToken.None);
        first.Keys.Should().Equal(8, 10);
        first[8].Should().Be("Zulu!");
        var second = await Invoke<Task<IReadOnlyDictionary<int, string>>>(operations, "ReadAsync", request with { SortBy = "name" }, CancellationToken.None);
        second.Keys.Should().Equal(10, 8);
        ids.Should().Equal(3, 1, 3);
        fixture.Sql.Commands.Should().HaveCount(2).And.OnlyContain(command => command.Sql.Contains("ORDER BY") && command.Sql.Contains("WHERE"));
    }

    private static void ConfigureMappings(RepositoryEntityBuilder<PipelineRow> entity, bool cache = false)
    {
        RepositoryResultQueryBuilder<PipelineRow> Root(string name)
        {
            var root = entity.HasQuery(name);
            if (cache)
            {
                root.Cache();
            }

            return root.Results<PipelineRow>().OrderBy(row => row.Id);
        }

        Root("Both").Dictionary((PipelineRow row, TerminalRequest request) => row.Id + request.Adjustment, (PipelineRow row, TerminalRequest format) => row.Name + format.Suffix);
        Root("Value").Dictionary(row => row.Id, (PipelineRow row, TerminalRequest request) => row.Name + request.Suffix);
        Root("Key").Dictionary((PipelineRow row, TerminalRequest request) => row.Id + request.Adjustment, row => row.Name);
        entity.HasQuery("Rows").Select(row => row.Id).Dictionary<int, TerminalRequest>((id, request) => id + request.Adjustment);
        Root("First").Dictionary((PipelineRow row, TerminalRequest request) => row.Category + request.Suffix, (PipelineRow row, TerminalRequest format) => row.Name + format.Suffix, RepositoryDuplicateKeyBehavior.KeepFirst);
        Root("Last").Dictionary((PipelineRow row, TerminalRequest request) => row.Category + request.Suffix, (PipelineRow row, TerminalRequest format) => row.Name + format.Suffix, RepositoryDuplicateKeyBehavior.KeepLast);
        Root("Lookup").Lookup((PipelineRow row, TerminalRequest request) => row.Category + request.Suffix, (PipelineRow row, TerminalRequest format) => row.Name + format.Suffix);
        Root("LookupKey").Lookup((PipelineRow row, TerminalRequest request) => row.Category + request.Suffix, row => row.Name);
        Root("LookupValue").Lookup(row => row.Category, (PipelineRow row, TerminalRequest request) => row.Name + request.Suffix);
        entity.HasQuery("LookupRows").Select(row => row.Id).Lookup<int, TerminalRequest>((id, request) => id + request.Adjustment);
    }

    private static void ConfigureNumericTerminals(RepositoryEntityBuilder<PipelineRow> entity)
    {
        foreach (var numeric in new[] { typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal) })
        {
            foreach (var nullable in new[] { false, true })
            {
                foreach (var terminal in new[] { "Sum", "Average" })
                {
                    var valueType = nullable ? typeof(Nullable<>).MakeGenericType(numeric) : numeric;
                    var row = Expression.Parameter(typeof(int), "amount");
                    var request = Expression.Parameter(typeof(TerminalRequest), "request");
                    var body = Expression.Convert(Expression.Add(row, Expression.Property(request, nameof(TerminalRequest.Adjustment))), valueType);
                    var query = entity.HasQuery(terminal + numeric.Name + (nullable ? "Nullable" : "")).Select(row => row.Amount);
                    var method = typeof(RepositoryResultQueryBuilder<int>).GetMethods().Single(method => method.Name == terminal && method.GetGenericArguments().Length == 1 &&
                                                                                                         method.GetParameters()[0].ParameterType.GetGenericArguments()[0].GetGenericArguments()[2] == valueType).MakeGenericMethod(typeof(TerminalRequest));
                    method.Invoke(query, [Expression.Lambda(body, row, request)]);
                }
            }
        }

        entity.HasQuery("Maximum").Select(row => row.Amount).Max<int, TerminalRequest>((amount, request) => amount + request.Adjustment);
        entity.HasQuery("Minimum").Select(row => row.Amount).Min<int?, TerminalRequest>((amount, request) => (int?)(amount + request.Adjustment));
    }

    private static RepositoryModelBuilder Builder(string surface = "closed")
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        if (surface == "closed")
        {
            builder.Configure(options => options.UseClosedRepositories("Terminal.Contracts"));
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
            .Append(typeof(RequestTerminalTests).Assembly.Location).Append(typeof(DbContext).Assembly.Location)
            .Append(typeof(RelationalQueryableExtensions).Assembly.Location).Append(typeof(IDbContextLeaseProvider<>).Assembly.Location)
            .Append(typeof(PagedResult<>).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create("RequestTerminals_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)),
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
        public List<(string Sql, bool Transaction, int Timeout, object?[] Values)> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add((command.CommandText, command.Transaction is not null, command.CommandTimeout, command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).ToArray()));
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

public sealed record TerminalRequest(int Adjustment, string Suffix, int Minimum);

public sealed record TerminalEfRequest(DbContext Context);

public readonly record struct TerminalId(int Value);

public sealed record TerminalAdaptedRequest(TerminalId Adjustment);

public sealed record TerminalCompositeRequest(int[] Ids, string? SortBy, int Adjustment, string Suffix);

public interface ITerminalPort
{
    Task<IReadOnlyDictionary<int, int>> ReadAsync(TerminalRequest request, CancellationToken cancellationToken = default);
}

public sealed class TerminalQueryRoot : IRepositoryQueryRoot<PipelineContext, TerminalRequest, ConditionalSummary>
{
    public bool SawTransaction { get; private set; }

    public IQueryable<ConditionalSummary> CreateQuery(PipelineContext context, TerminalRequest request)
    {
        SawTransaction = context.Database.CurrentTransaction != null;
        return context.Rows.AsNoTracking().Where(row => row.Id >= request.Minimum).Select(row => new ConditionalSummary { Id = row.Id, Name = row.Name, Amount = row.Amount });
    }
}