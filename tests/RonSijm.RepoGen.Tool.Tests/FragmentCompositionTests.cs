using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
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

public sealed class FragmentCompositionTests
{
    [Theory]
    [InlineData("and", 10, false)]
    [InlineData("and", 30, true)]
    [InlineData("or", 10, true)]
    [InlineData("or", 70, true)]
    [InlineData("not", 10, true)]
    [InlineData("not", 30, false)]
    public void StaticCompositionIsImmutableAndPreservesBooleanSemantics(string operation, int amount, bool expected)
    {
        var builder = new RepositoryModelBuilder();
        var entity = builder.Entity<PipelineRow>();
        var lower = entity.HasFragment("Lower", row => row.Amount >= 20);
        var upper = entity.HasFragment("Upper", other => other.Amount <= 40);
        var composed = operation switch { "and" => lower.And(upper), "or" => lower.Or(upper), _ => lower.Not() };
        entity.HasQuery("Composed").Use(composed);
        entity.HasQuery("Original").Use(lower);
        var queries = builder.Build().Entities.Single().Queries;
        Evaluate(queries[0].Predicates[0], new PipelineRow { Amount = amount }).Should().Be(expected);
        Evaluate(queries[1].Predicates[0], new PipelineRow { Amount = amount }).Should().Be(amount >= 20);
        composed.Name.Should().Be("Lower");
        new InvocationFinder().Contains(queries[0].Predicates[0]).Should().BeFalse();
    }

    [Theory]
    [InlineData("typedAnd", 30, true)]
    [InlineData("typedAnd", 60, false)]
    [InlineData("typedOr", 10, true)]
    [InlineData("typedNot", 30, false)]
    [InlineData("leftStaticAnd", 30, true)]
    [InlineData("leftStaticOr", 10, true)]
    [InlineData("rightStaticAnd", 30, true)]
    [InlineData("rightStaticOr", 10, true)]
    public void RequestCompositionUnifiesParameterIdentitiesAndLocalNames(string operation, int amount, bool expected)
    {
        var builder = new RepositoryModelBuilder();
        var entity = builder.Entity<PipelineRow>();
        var lower = entity.HasFragment<FragmentRequest>("Lower", (row, bounds) => row.Amount >= bounds.Minimum);
        var upper = entity.HasFragment<FragmentRequest>("Upper", (other, limits) => other.Amount <= limits.Maximum);
        var fixedUpper = entity.HasFragment("FixedUpper", other => other.Amount <= 40);
        var composed = operation switch
        {
            "typedAnd" => lower.And(upper),
            "typedOr" => lower.Or(upper),
            "typedNot" => lower.Not(),
            "leftStaticAnd" => fixedUpper.And(lower),
            "leftStaticOr" => fixedUpper.Or(lower),
            "rightStaticAnd" => lower.And(fixedUpper),
            _ => lower.Or(fixedUpper)
        };
        entity.HasQuery("Composed").Use(composed.WithRequestName("request"));
        entity.HasQuery("Original").Use(lower);
        var queries = builder.Build().Entities.Single().Queries;
        var predicate = queries[0].Predicates[0];
        predicate.Parameters.Should().HaveCount(2);
        predicate.Parameters[1].Name.Should().Be("request");
        Evaluate(predicate, new PipelineRow { Amount = amount }, Request()).Should().Be(expected);
        queries[1].Predicates[0].Parameters[1].Name.Should().Be("bounds");
        new InvocationFinder().Contains(predicate).Should().BeFalse();
    }

    [Fact]
    public void RebindingDoesNotCaptureAnIncomingRequestInANestedLambda()
    {
        var builder = new RepositoryModelBuilder();
        var entity = builder.Entity<PipelineRow>();
        var row = Expression.Parameter(typeof(PipelineRow), "row");
        var request = Expression.Parameter(typeof(int), "request");
        var other = Expression.Parameter(typeof(PipelineRow), "other");
        var input = Expression.Parameter(typeof(int), "input");
        var lower = entity.HasFragment("Lower", Expression.Lambda<Func<PipelineRow, int, bool>>(
            Expression.GreaterThanOrEqual(Expression.Property(row, nameof(PipelineRow.Amount)), request), row, request));
        // Deliberately reuse the left request object as a right nested binder. Composition must alpha-rename it.
        var nested = Expression.Lambda<Func<int, bool>>(Expression.AndAlso(
            Expression.Equal(request, Expression.Property(other, nameof(PipelineRow.Amount))),
            Expression.Equal(Expression.Property(other, nameof(PipelineRow.Id)), input)), request);
        var any = Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), [typeof(int)],
            Expression.NewArrayInit(typeof(int), Expression.Constant(3)), nested);
        var right = entity.HasFragment("Nested", Expression.Lambda<Func<PipelineRow, int, bool>>(any, other, input));
        entity.HasQuery("Composed").Use(lower.And(right));
        var predicate = builder.Build().Entities.Single().Queries.Single().Predicates.Single();
        Evaluate(predicate, new PipelineRow { Id = 2, Amount = 3 }, 2).Should().BeTrue();
        Evaluate(predicate, new PipelineRow { Id = 3, Amount = 3 }, 2).Should().BeFalse();
    }

    [Fact]
    public void NestedBindersShadowingOriginalParametersRemainBoundLocally()
    {
        var builder = new RepositoryModelBuilder();
        var entity = builder.Entity<PipelineRow>();
        var left = entity.HasFragment<int>("Lower", (row, request) => row.Amount >= request);
        var row = Expression.Parameter(typeof(PipelineRow), "row");
        var input = Expression.Parameter(typeof(int), "input");
        var nested = Expression.Lambda<Func<int, bool>>(Expression.Equal(input, Expression.Property(row, nameof(PipelineRow.Amount))), input);
        var any = Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), [typeof(int)],
            Expression.NewArrayInit(typeof(int), Expression.Constant(3)), nested);
        var right = entity.HasFragment("Nested", Expression.Lambda<Func<PipelineRow, int, bool>>(any, row, input));
        entity.HasQuery("Composed").Use(left.And(right));
        Evaluate(builder.Build().Entities.Single().Queries.Single().Predicates.Single(), new PipelineRow { Amount = 3 }, 2).Should().BeTrue();
    }

    [Fact]
    public void ModelFragmentsDoNotRegisterFakeEntitiesAndDuplicatesAreScopedByRowAndRequestType()
    {
        var builder = new RepositoryModelBuilder();
        builder.HasFragment<int>("Positive", value => value > 0);
        builder.HasFragment<string>("Positive", value => value.Length > 0);
        builder.HasFragment<int, int>("Positive", (value, minimum) => value > minimum);
        builder.HasFragment<int, long>("Positive", (value, minimum) => value > minimum);
        builder.HasFragment<int>("positive", value => value >= 0);
        builder.Build().Entities.Should().BeEmpty();
        Action duplicate = () => builder.HasFragment<int>(" Positive ", value => value >= 0);
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("RGD2201:*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("request-name")]
    [InlineData("1request")]
    [InlineData("request.Name")]
    public void InvalidRequestAliasesFailAtConfigurationTime(string name)
    {
        var fragment = new RepositoryModelBuilder().HasFragment<int, int>("Lower", (value, minimum) => value >= minimum);
        Action rename = () => fragment.WithRequestName(name);
        rename.Should().Throw<ArgumentException>().WithMessage("RGD2202:*");
    }

    [Fact]
    public void KeywordRequestAliasesAreEscapedByTheExistingRenderer()
    {
        using var context = Context();
        var builder = Builder();
        var fragment = builder.HasFragment<PipelineRow, int>("Minimum", (row, minimum) => row.Amount >= minimum).WithRequestName(" event ");
        builder.Entity<PipelineRow>().HasQuery("Find").Use(fragment).Select(row => row.Id).List();
        var assembly = ClosedRepositoryTests.Compile(Model(context, builder), rejectWarnings: true);
        assembly.GetType(typeof(PipelineRow).Namespace + ".PipelineRowRepositoryOperations")!.GetMethod("FindAsync")!.GetParameters()[0].Name.Should().Be("event");
    }

    [Fact]
    public void NullArgumentsAndEmptyNamesAreRejectedWithoutChangingTheQuery()
    {
        var builder = new RepositoryModelBuilder();
        var entity = builder.Entity<PipelineRow>();
        var fragment = entity.HasFragment("Positive", row => row.Amount > 0);
        var typed = entity.HasFragment<int>("Lower", (row, minimum) => row.Amount >= minimum);
        var query = entity.HasQuery("Read");
        var result = entity.HasQuery("Result").Select(row => row.Amount);
        var scalar = builder.HasFragment<int>("Scalar", value => value > 0);
        Action[] invalid =
        [
            () => builder.HasFragment<int>("Invalid", null!), () => builder.HasFragment<int, int>("Invalid", null!),
            () => builder.HasFragment<int>(" ", value => value > 0), () => fragment.And((RepositoryQueryFragment<PipelineRow>)null!),
            () => fragment.Or((RepositoryQueryFragment<PipelineRow>)null!), () => fragment.And<int>(null!), () => fragment.Or<int>(null!),
            () => typed.And((RepositoryQueryFragment<PipelineRow, int>)null!), () => typed.Or((RepositoryQueryFragment<PipelineRow, int>)null!),
            () => typed.And((RepositoryQueryFragment<PipelineRow>)null!), () => typed.Or((RepositoryQueryFragment<PipelineRow>)null!),
            () => query.UseIf<int>(null!, fragment), () => query.UseIf<int>(null!, typed),
            () => query.UseIf<int>(request => request > 0, (RepositoryQueryFragment<PipelineRow>)null!),
            () => query.UseIf<int>(request => request > 0, (RepositoryQueryFragment<PipelineRow, int>)null!),
            () => result.Use((RepositoryQueryFragment<int>)null!), () => result.Use<int>(null!),
            () => result.UseIf<int>(null!, scalar), () => result.UseIf<int>(null!, builder.HasFragment<int, int>("TypedScalar", (value, request) => value >= request)),
            () => result.UseIf<int>(request => request > 0, (RepositoryQueryFragment<int>)null!),
            () => result.UseIf<int>(request => request > 0, (RepositoryQueryFragment<int, int>)null!)
        ];
        foreach (var action in invalid)
        {
            action.Should().Throw<ArgumentException>();
        }

        var queries = builder.Build().Entities.Single().Queries;
        queries[0].Predicates.Should().BeEmpty();
        queries[0].ConditionalPredicates.Should().BeEmpty();
        queries[1].ResultStages.Should().BeEmpty();
    }

    [Fact]
    public void ReusedFragmentsCannotMutateStaleResultStages()
    {
        var builder = new RepositoryModelBuilder();
        var fragment = builder.HasFragment<int>("Positive", value => value > 0);
        var typed = builder.HasFragment<int, int>("Minimum", (value, minimum) => value > minimum);
        var stale = builder.Entity<PipelineRow>().HasQuery("Find").Select(row => row.Amount);
        stale.Select(value => (long)value);
        Action[] invalid =
        [
            () => stale.Use(fragment), () => stale.Use(typed),
            () => stale.UseIf<int>(request => request > 0, fragment), () => stale.UseIf<int>(request => request > 0, typed)
        ];
        foreach (var action in invalid)
        {
            action.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        }
    }

    [Theory]
    [InlineData("closed", RepositoryOutputLayout.SingleFile)]
    [InlineData("closed", RepositoryOutputLayout.PerEntity)]
    [InlineData("closed", RepositoryOutputLayout.PerContract)]
    [InlineData("cqrs", RepositoryOutputLayout.SingleFile)]
    [InlineData("cqrs", RepositoryOutputLayout.PerEntity)]
    [InlineData("compatibility", RepositoryOutputLayout.SingleFile)]
    [InlineData("compatibility", RepositoryOutputLayout.PerEntity)]
    public void FragmentsCompileWithoutWarningsInEverySupportedSurfaceAndLayout(string surface, RepositoryOutputLayout layout)
    {
        using var context = Context();
        var builder = Builder(surface);
        ConfigureQueries(builder);
        var model = Model(context, builder, surface != "closed");
        var bundle = RepositoryGenerator.Generate(model, layout);
        bundle.Files.Should().Equal(RepositoryGenerator.Generate(model, layout).Files);
        CompileFiles(bundle.Files.Where(file => file.Kind != RepositoryOutputKind.Manifest).Select(file => file.Contents));
    }

    [Fact]
    public void UnusedGlobalFragmentsAndEquivalentReuseDoNotChangeGeneratedDefaults()
    {
        using var context = Context();
        var plain = Builder();
        var reuse = Builder();
        plain.Entity<PipelineRow>().HasQuery("Find").Where<FragmentRequest>((row, request) => row.Amount >= request.Minimum).Select(row => row.Id).List();
        var fragment = reuse.HasFragment<PipelineRow, FragmentRequest>("Minimum", (row, local) => row.Amount >= local.Minimum);
        reuse.HasFragment<PipelineReport>("Unused", report => report.Count > 0);
        reuse.Entity<PipelineRow>().HasQuery("Find").Use(fragment.WithRequestName("request")).Select(row => row.Id).List();
        RepositoryGenerator.Generate(Model(context, plain)).Files.Should().Equal(RepositoryGenerator.Generate(Model(context, reuse)).Files);
    }

    [Fact]
    public async Task ComposedRequestsAndMixedStaticFragmentsExecuteOneServerQuery()
    {
        await using var harness = await Harness.CreateAsync();
        (await harness.Call<Task<IReadOnlyList<int>>>("WindowAsync", Request() with { Minimum = 15, Maximum = 55 }, CancellationToken.None)).Should().Equal(2, 3);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("WHERE").And.Contain(" AND ");
        harness.Sql.Commands.Clear();
        (await harness.Call<Task<IReadOnlyList<int>>>("EitherAsync", Request() with { Category = "C", Maximum = 30 }, CancellationToken.None)).Should().Equal(2, 3, 5, 6);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain(" OR ");
        (await harness.Call<Task<IReadOnlyList<int>>>("MixedAsync", Request() with { Category = "B" }, CancellationToken.None)).Should().Equal(2, 3, 4);
        harness.Provider.Contexts.Should().OnlyContain(item => item.DisposeCount == 1);
        harness.Context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ConditionalFragmentsShareOneRequestAndInactiveClausesAreNotSentToSql()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Operations.GetType().GetMethod("OptionalAsync")!.GetParameters().Select(parameter => parameter.ParameterType)
            .Should().Equal(typeof(FragmentRequest), typeof(CancellationToken));
        (await harness.Call<Task<IReadOnlyList<int>>>("OptionalAsync", Request(), CancellationToken.None)).Should().HaveCount(7);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().NotContain("WHERE");
        harness.Sql.Commands.Clear();
        var request = Request() with { ApplyMinimum = true, ApplyCategory = true, Category = "B" };
        (await harness.Call<Task<IReadOnlyList<int>>>("OptionalAsync", request, CancellationToken.None)).Should().Equal(3, 4);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("WHERE").And.Contain("Category").And.Contain("Amount");
        harness.Sql.Commands.Clear();
        (await harness.Call<Task<IReadOnlyList<int>>>("OptionalAsync", Request() with { HideD = true }, CancellationToken.None)).Should().Equal(1, 2, 3, 4, 5, 6);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().NotContain("Amount");
    }

    [Fact]
    public async Task GlobalDtoAndScalarFragmentsStayInsideProjectedAndFromPipelines()
    {
        await using var harness = await Harness.CreateAsync();
        var request = Request() with { ApplyMinimum = true, Minimum = 30 };
        (await harness.Call<Task<IReadOnlyList<int>>>("AmountsAsync", request, CancellationToken.None)).Should().Equal(30, 40);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("WHERE").And.NotContain("Name");
        harness.Sql.Commands.Clear();
        (await harness.Call<Task<IReadOnlyList<int>>>("FromAsync", request, CancellationToken.None)).Should().Equal(3, 4);
        harness.Sql.Commands.Should().ContainSingle();
        (await harness.Call<Task<IReadOnlyList<PipelineReport>>>("DtosAsync", Request() with { Minimum = 50 }, CancellationToken.None))
            .Select(report => report.Total).Should().Equal(50, 60, 70);
    }

    [Fact]
    public async Task GroupedConditionalFragmentsGenerateHavingCountedPagesAndPrunedDictionaries()
    {
        await using var harness = await Harness.CreateAsync();
        (await harness.Call<Task<IReadOnlyList<PipelineReport>>>("GroupsAsync", Request(), CancellationToken.None)).Should().HaveCount(4);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().NotContain("HAVING");
        harness.Sql.Commands.Clear();
        var request = Request() with { MinimumCount = 2 };
        var groups = await harness.Call<Task<IReadOnlyList<PipelineReport>>>("GroupsAsync", request, CancellationToken.None);
        groups.Select(report => report.Category).Should().Equal("B", "C");
        groups.Select(report => report.Total).Should().Equal(70, 110);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("HAVING");
        harness.Sql.Commands.Clear();
        var page = await harness.Call<Task<PagedResult<PipelineReport>>>("GroupPageAsync", request, 2, 1, CancellationToken.None);
        page.TotalCount.Should().Be(2);
        page.Items.Should().ContainSingle().Which.Category.Should().Be("C");
        harness.Sql.Commands.Should().HaveCount(2).And.OnlyContain(command => command.Contains("HAVING", StringComparison.Ordinal));
        var dictionary = await harness.Call<Task<IReadOnlyDictionary<string, int>>>("GroupDictionaryAsync", request, CancellationToken.None);
        dictionary.Should().BeEquivalentTo(new Dictionary<string, int> { ["B"] = 70, ["C"] = 110 });
    }

    [Fact]
    public async Task RequestGuardsAndCancellationRunBeforeLeaseAcquisitionAndStreamDisposalIsPreserved()
    {
        await using var harness = await Harness.CreateAsync();
        Func<Task> invalid = () => harness.Call<Task<IReadOnlyList<int>>>("OptionalAsync", Request() with { Minimum = -1 }, CancellationToken.None);
        await invalid.Should().ThrowAsync<ArgumentException>().WithMessage("Invalid amount window.*");
        Func<Task> missing = () => harness.Call<Task<IReadOnlyList<int>>>("OptionalAsync", null!, CancellationToken.None);
        await missing.Should().ThrowAsync<ArgumentException>().WithMessage("Invalid amount window.*");
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        Func<Task> cancellation = () => harness.Call<Task<IReadOnlyList<int>>>("OptionalAsync", Request(), canceled.Token);
        await cancellation.Should().ThrowAsync<OperationCanceledException>();
        harness.Provider.Contexts.Should().BeEmpty();
        harness.Sql.Commands.Should().BeEmpty();
        var stream = harness.Call<IAsyncEnumerable<int>>("StreamAsync", Request(), CancellationToken.None);
        harness.Provider.Contexts.Should().BeEmpty();
        await using (var enumerator = stream.GetAsyncEnumerator())
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            harness.Provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(0);
        }

        harness.Provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ComposedCommandPredicatesRemainMandatoryAndExecuteOneSetBasedStatement()
    {
        await using var harness = await Harness.CreateAsync();
        (await harness.Call<Task<int>>("DeleteWindowAsync", Request() with { Minimum = 30 }, CancellationToken.None)).Should().Be(2);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("DELETE").And.Contain("WHERE");
        (await harness.Context.Rows.OrderBy(row => row.Id).Select(row => row.Id).ToListAsync()).Should().Equal(1, 2, 5, 6, 7);
    }

    [Fact]
    public async Task FragmentConditionsDoNotBypassIndependentRootVisibility()
    {
        await using var harness = await Harness.CreateAsync(builder => builder.Entity<PipelineRow>().HasVisibilityPolicy("HideD", row => row.Category != "D"));
        (await harness.Call<Task<IReadOnlyList<int>>>("OptionalAsync", Request(), CancellationToken.None)).Should().HaveCount(6);
        (await harness.Call<Task<IReadOnlyList<int>>>("FromAsync", Request() with { Maximum = 100 }, CancellationToken.None)).Should().Equal(2, 3, 4, 5, 6);
    }

    [Fact]
    public async Task ComposedTypeAdapterMarkersExpandBeforeSqlTranslation()
    {
        await using var harness = await Harness.CreateAsync(builder =>
        {
            var adapter = builder.HasTypeAdapter<int, ApplicationRowId>(value => new ApplicationRowId(value), value => value.Value);
            var minimum = builder.HasFragment<PipelineRow, ApplicationRowId>("IdMinimum", (row, lower) => row.Id >= adapter.ToModel(lower));
            var maximum = builder.HasFragment<PipelineRow>("IdMaximum", row => row.Id <= 3);
            builder.Entity<PipelineRow>().HasQuery("Ids").Use(minimum.And(maximum).WithRequestName("id"))
                .OrderBy(row => row.Id).Select(row => row.Id).List();
        });
        (await harness.Call<Task<IReadOnlyList<int>>>("IdsAsync", new ApplicationRowId(2), CancellationToken.None)).Should().Equal(2, 3);
        harness.Sql.Commands.Should().ContainSingle().Which.Should().Contain("WHERE");
    }

    [Fact]
    public async Task UnsupportedComposedFiltersNeverFallBackToClientEvaluation()
    {
        await using var harness = await Harness.CreateAsync(builder =>
        {
            var unsupported = builder.HasFragment<string>("Decorated", name => PipelineTransforms.Decorate(name) == "[Alpha]");
            var shortName = builder.HasFragment<string>("Short", name => name.Length < 10);
            builder.Entity<PipelineRow>().HasQuery("Unsupported").Select(row => row.Name)
                .UseIf<FragmentRequest>(request => request.ApplyCategory, unsupported.And(shortName)).List();
        });
        (await harness.Call<Task<IReadOnlyList<string>>>("UnsupportedAsync", Request(), CancellationToken.None)).Should().HaveCount(7);
        harness.Sql.Commands.Clear();
        Func<Task> unsupported = () => harness.Call<Task<IReadOnlyList<string>>>("UnsupportedAsync", Request() with { ApplyCategory = true }, CancellationToken.None);
        await unsupported.Should().ThrowAsync<InvalidOperationException>().WithMessage("*could not be translated*");
        harness.Sql.Commands.Should().BeEmpty();
        harness.Provider.Contexts.Should().OnlyContain(context => context.DisposeCount == 1);
    }

    [Fact]
    public void ClosedContractsExposeNoFragmentsExpressionsEntitiesOrEfDependency()
    {
        using var context = Context();
        var builder = Builder();
        ConfigureQueries(builder);
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(Model(context, builder));
        contracts.Should().NotContain("RepositoryQueryFragment").And.NotContain("Expression").And.NotContain("IQueryable")
            .And.NotContain("EntityFrameworkCore").And.NotContain("PipelineRow>");
        const string dtos = """
                            namespace RonSijm.RepoGen.Tool.Tests {
                                public sealed class PipelineReport { public string Category {get;set;} = ""; public int Count {get;set;} public int Total {get;set;} }
                                public sealed record FragmentRequest(int Minimum,int Maximum,string? Category,int MinimumCount,bool ApplyMinimum,bool ApplyCategory,bool HideD);
                            }
                            """;
        var compilation = CSharpCompilation.Create("FragmentConsumer", [CSharpSyntaxTree.ParseText(contracts), CSharpSyntaxTree.ParseText(dtos)],
            ClosedRepositoryTests.PlatformReferences().Append(MetadataReference.CreateFromFile(typeof(PagedResult<>).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    private static bool Evaluate(LambdaExpression expression, params object[] arguments) => (bool)expression.Compile().DynamicInvoke(arguments)!;
    private static FragmentRequest Request() => new(20, 40, null, 0, false, false, false);
    private static PipelineContext Context() => new(new DbContextOptionsBuilder<PipelineContext>().UseSqlite("Data Source=:memory:").Options);

    private static RepositoryModelBuilder Builder(string surface = "closed")
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options =>
        {
            options.UseSimpleDefaults().GenerateOnlyConfiguredEntities();
            if (surface == "closed")
            {
                options.UseClosedRepositories("Fragments.Contracts").UseAsyncContextLeases();
            }

            if (surface == "cqrs")
            {
                options.UseCqrsSelectors();
            }
        });
        return builder;
    }

    private static RepositoryGenerationModel Model(PipelineContext context, RepositoryModelBuilder builder, bool selectors = false) =>
        RepositoryModelFactory.Create(context, typeof(PipelineContext).Assembly, generateSelectors: selectors, design: builder.Build());

    private static void ConfigureQueries(RepositoryModelBuilder builder)
    {
        var entity = builder.Entity<PipelineRow>();
        var lower = entity.HasFragment<FragmentRequest>("Lower", (row, bounds) => row.Amount >= bounds.Minimum);
        var upper = entity.HasFragment<FragmentRequest>("Upper", (other, limits) => other.Amount <= limits.Maximum);
        var category = entity.HasFragment<FragmentRequest>("Category", (item, selection) => item.Category == selection.Category);
        var a = entity.HasFragment("CategoryA", row => row.Category == "A");
        var b = entity.HasFragment("CategoryB", other => other.Category == "B");
        var delta = entity.HasFragment("Delta", other => other.Name == "Delta");
        var notD = entity.HasFragment("HideD", other => other.Category == "D").Not();
        var window = lower.And(upper).WithRequestName("request");
        entity.HasQuery("Window").Use(window.And(a.Or(b)).And(delta.Not())).OrderBy(row => row.Id).Select(row => row.Id).List();
        entity.HasQuery("Either").Use(category.Or(window).WithRequestName("request")).OrderBy(row => row.Id).Select(row => row.Id).List();
        entity.HasQuery("Mixed").Use(a.Or(category).And(lower).WithRequestName("request")).OrderBy(row => row.Id).Select(row => row.Id).List();
        entity.HasQuery("Optional").Validate<FragmentRequest>(request => request != null && request.Minimum >= 0 && request.Maximum >= request.Minimum, "Invalid amount window.")
            .UseIf<FragmentRequest>(request => request.ApplyMinimum, lower.And(upper))
            .UseIf<FragmentRequest>(request => request.ApplyCategory, category)
            .UseIf<FragmentRequest>(request => request.HideD, notD).OrderBy(row => row.Id).Select(row => row.Id).List();
        var scalar = builder.HasFragment<int>("Positive", amount => amount > 0);
        var scalarWindow = builder.HasFragment<int, FragmentRequest>("ScalarWindow", (amount, criteria) => amount >= criteria.Minimum && amount <= criteria.Maximum);
        entity.HasQuery("Amounts").Select(row => row.Amount).Use(scalar).UseIf<FragmentRequest>(request => request.ApplyMinimum, scalarWindow).OrderBy(amount => amount).List();
        entity.HasQuery("From").From<FragmentRequest, PipelineRow>((sources, request) => sources.Set<PipelineRow>())
            .Use(window).OrderBy(row => row.Id).Select(row => row.Id).List();
        var dto = builder.HasFragment<PipelineReport, FragmentRequest>("DtoMinimum", (report, criteria) => report.Total >= criteria.Minimum).WithRequestName("request");
        entity.HasQuery("Dtos").Select(row => new PipelineReport { Category = row.Category, Count = 1, Total = row.Amount })
            .Use(dto).OrderBy(report => report.Total).List();
        var count = builder.HasFragment<PipelineReport, FragmentRequest>("MinimumCount", (report, criteria) => report.Count >= criteria.MinimumCount);
        var total = builder.HasFragment<PipelineReport>("MinimumTotal", report => report.Total >= 50);
        foreach (var name in new[] { "Groups", "GroupPage", "GroupDictionary" })
        {
            var groups = entity.HasQuery(name).GroupBy(row => row.Category, group => new PipelineReport { Category = group.Key, Count = group.Count(), Total = group.Sum(row => row.Amount) })
                .Results<PipelineReport>().UseIf<FragmentRequest>(request => request.MinimumCount > 0, count.And(total)).OrderBy(report => report.Category);
            if (name == "GroupPage")
            {
                groups.Paged();
            }
            else if (name == "GroupDictionary")
            {
                groups.Dictionary(report => report.Category, report => report.Total);
            }
            else
            {
                groups.List();
            }
        }

        entity.HasQuery("Stream").Use(window).OrderBy(row => row.Id).Select(row => row.Id).Stream();
        entity.HasCommand("DeleteWindow").Use(window).Delete();
    }

    private static Assembly CompileFiles(IEnumerable<string> sources)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(FragmentCompositionTests).Assembly.Location).Append(typeof(DbContext).Assembly.Location)
            .Append(typeof(RelationalQueryableExtensions).Assembly.Location).Append(typeof(IDbContextLeaseProvider<>).Assembly.Location)
            .Append(typeof(PagedResult<>).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create("Fragments_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            paths.Select(path => MetadataReference.CreateFromFile(path)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error || diagnostic.Severity == DiagnosticSeverity.Warning && diagnostic.Id != "CS1701").Should().BeEmpty();
        output.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(output);
    }

    private sealed class InvocationFinder : ExpressionVisitor
    {
        private bool found;

        public bool Contains(Expression expression)
        {
            Visit(expression);
            return found;
        }

        protected override Expression VisitInvocation(InvocationExpression node)
        {
            found = true;
            return base.VisitInvocation(node);
        }
    }

    private sealed class Harness(SqliteConnection connection, PipelineContext context, SqlRecorder sql, Provider provider, object operations) : IAsyncDisposable
    {
        private static readonly string[] Names = ["Alpha", "Beta", "Gamma", "Delta", "Echo", "Foxtrot", "Eta"];
        public PipelineContext Context { get; } = context;
        public SqlRecorder Sql { get; } = sql;
        public Provider Provider { get; } = provider;
        public object Operations { get; } = operations;
        public TResult Call<TResult>(string method, params object[] arguments) => (TResult)Operations.GetType().GetMethod(method)!.Invoke(Operations, arguments)!;

        public static async Task<Harness> CreateAsync(Action<RepositoryModelBuilder>? configure = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var sql = new SqlRecorder();
            var context = new PipelineContext(new DbContextOptionsBuilder<PipelineContext>().UseSqlite(connection).AddInterceptors(sql).Options);
            await context.Database.EnsureCreatedAsync();
            context.AddRange(Names.Select((name, index) =>
                new PipelineRow { Id = index + 1, Category = ((char)('A' + index / 2)).ToString(), Amount = (index + 1) * 10, Name = name }));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            sql.Commands.Clear();
            var builder = Builder();
            ConfigureQueries(builder);
            configure?.Invoke(builder);
            var assembly = ClosedRepositoryTests.Compile(Model(context, builder), rejectWarnings: true);
            var provider = new Provider(connection, sql);
            var operations = Activator.CreateInstance(assembly.GetType(typeof(PipelineRow).Namespace + ".PipelineRowRepositoryOperations")!, provider)!;
            return new Harness(connection, context, sql, provider, operations);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class Provider(SqliteConnection connection, SqlRecorder sql) : IDbContextLeaseProvider<PipelineContext>
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

    private sealed class SqlRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}

public sealed record FragmentRequest(int Minimum, int Maximum, string? Category, int MinimumCount, bool ApplyMinimum, bool ApplyCategory, bool HideD);