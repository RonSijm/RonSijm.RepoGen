using System.Data.Common;
using System.Linq.Expressions;
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

public sealed class ConditionalQueryStageTests
{
    [Theory]
    [InlineData("OrderBy", "condition")]
    [InlineData("OrderBy", "selector")]
    [InlineData("OrderByDescending", "condition")]
    [InlineData("OrderByDescending", "selector")]
    [InlineData("ThenBy", "condition")]
    [InlineData("ThenBy", "selector")]
    [InlineData("ThenByDescending", "condition")]
    [InlineData("ThenByDescending", "selector")]
    [InlineData("Skip", "condition")]
    [InlineData("Skip", "selector")]
    [InlineData("Take", "condition")]
    [InlineData("Take", "selector")]
    public void NullArgumentsFailWithoutAddingAStage(string operation, string argument)
    {
        var builder = new RepositoryModelBuilder();
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Results<PipelineRow>();
        Expression<Func<ConditionalRequest, bool>> condition = argument == "condition" ? null! : request => request.Rank;
        Expression<Func<PipelineRow, int>> key = argument == "selector" ? null! : row => row.Amount;
        Expression<Func<ConditionalRequest, int>> count = argument == "selector" ? null! : request => request.Offset!.Value;
        Action add = operation switch
        {
            "OrderBy" => () => query.OrderByIf(condition, key),
            "OrderByDescending" => () => query.OrderByDescendingIf(condition, key),
            "ThenBy" => () => query.ThenByIf(condition, key),
            "ThenByDescending" => () => query.ThenByDescendingIf(condition, key),
            "Skip" => () => query.SkipIf(condition, count),
            _ => () => query.TakeIf(condition, count)
        };
        add.Should().Throw<ArgumentNullException>();
        builder.Build().Entities.Single().Queries.Single().ResultStages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Skip")]
    [InlineData("Take")]
    public void NegativeConstantsAreRejectedEvenForDisabledBranches(string operation)
    {
        var query = new RepositoryModelBuilder().Entity<PipelineRow>().HasQuery("Read").Results<PipelineRow>();
        Action add = operation == "Skip" ? () => query.SkipIf<ConditionalRequest>(request => false, -1) : () => query.TakeIf<ConditionalRequest>(request => false, -1);
        add.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("OrderBy")]
    [InlineData("ThenBy")]
    [InlineData("Skip")]
    [InlineData("Take")]
    public void SupersededBuildersCannotMutateConditionalStages(string operation)
    {
        var builder = new RepositoryModelBuilder();
        var original = builder.Entity<PipelineRow>().HasQuery("Read").Results<PipelineRow>();
        original.Select(row => row.Name).List();
        Action add = operation switch
        {
            "OrderBy" => () => original.OrderByIf((ConditionalRequest request) => request.Rank, row => row.Id),
            "ThenBy" => () => original.ThenByIf((ConditionalRequest request) => request.Rank, row => row.Id),
            "Skip" => () => original.SkipIf<ConditionalRequest>(request => request.Skip, 1),
            _ => () => original.TakeIf<ConditionalRequest>(request => request.Take, 1)
        };
        add.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        builder.Build().Entities.Single().Queries.Single().ResultStages.Should().ContainSingle();
    }

    [Theory]
    [InlineData("ThenBy")]
    [InlineData("ThenByIf")]
    [InlineData("ThenByDescendingIf")]
    [InlineData("separated")]
    public void ConditionalOrderingCannotPretendToEstablishAnAdjacentOrderOnEveryBranch(string scenario)
    {
        using var context = MetadataContext();
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Bad").Results<PipelineRow>();
        if (scenario == "separated")
        {
            query.OrderBy(row => row.Id).Where(row => row.Amount > 0);
        }
        else
        {
            query.OrderByIf((ConditionalRequest request) => request.Rank, row => row.Amount);
        }

        if (scenario == "ThenBy")
        {
            query.ThenBy(row => row.Id);
        }
        else if (scenario == "ThenByDescendingIf")
        {
            query.ThenByDescendingIf((ConditionalRequest request) => request.Tie, row => row.Id);
        }
        else
        {
            query.ThenByIf((ConditionalRequest request) => request.Tie, row => row.Id);
        }

        query.Select(row => row.Id).List();
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*RGD2401*guaranteed on every branch*");
    }

    [Theory]
    [InlineData("root")]
    [InlineData("distinct")]
    [InlineData("group")]
    [InlineData("from")]
    public void GuardedPagesRequireAnUnconditionalFallbackOrder(string scenario)
    {
        using var context = MetadataContext();
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        if (scenario == "group")
        {
            entity.HasQuery("Bad").Results<PipelineRow>().GroupBy(row => row.Category, group => group.Count())
                .OrderByIf((ConditionalRequest request) => request.Rank, count => count).RequirePagingOrder().Paged();
        }
        else
        {
            var query = scenario == "from"
                ? entity.HasQuery("Bad").From(sources => sources.Set<PipelineRow>().Select(row => row.Id))
                : entity.HasQuery("Bad").Select(row => row.Id);
            if (scenario == "distinct")
            {
                query.OrderBy(id => id).Distinct();
            }

            query.OrderByIf((ConditionalRequest request) => request.Rank, id => id).RequirePagingOrder().Paged();
        }

        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*RGD1501*explicit paging OrderBy*");
    }

    [Fact]
    public void RequestAliasesAreReboundAndTheManifestDescribesOptionalStages()
    {
        using var context = MetadataContext();
        var builder = CoreBuilder();
        var model = Model(context, builder);
        var query = model.Entities.Single().Queries.Single(row => row.MethodName == "ReadAsync");
        query.Parameters.Should().ContainSingle().Which.Name.Should().Be("request");
        query.Pipeline!.Stages.Count(stage => stage.ConditionSource is not null).Should().Be(4);
        var designs = builder.Build().Entities.Single().Queries.Single(row => row.MethodName == "Read").ResultStages;
        foreach (var stage in designs.Where(stage => stage.Condition is not null && stage.Expression!.Parameters.Count == 2))
        {
            stage.Expression!.Parameters[1].Should().BeSameAs(stage.Condition!.Parameters[0]);
        }

        foreach (var stage in designs.Where(stage => stage.Kind is RepositoryQueryStageKind.Skip or RepositoryQueryStageKind.Take))
        {
            stage.Expression!.Parameters[0].Should().BeSameAs(stage.Condition!.Parameters[0]);
        }

        var manifest = JsonNode.Parse(RepositoryManifestRenderer.Render(model))!;
        var inventory = manifest["entities"]![0]!["queries"]!.AsArray().Single(row => row!["name"]!.GetValue<string>() == "ReadAsync")!["resultStages"]!.AsArray();
        inventory.Count(stage => stage!["conditional"]!.GetValue<bool>()).Should().Be(4);
        ClosedRepositorySourceRenderer.RenderContracts(model).Should().NotContain("OrderByIf").And.NotContain("SkipIf").And.NotContain("EntityFrameworkCore");
    }

    [Theory]
    [InlineData("closed", RepositoryOutputLayout.SingleFile)]
    [InlineData("closed", RepositoryOutputLayout.PerEntity)]
    [InlineData("closed", RepositoryOutputLayout.PerContract)]
    [InlineData("cqrs", RepositoryOutputLayout.SingleFile)]
    [InlineData("cqrs", RepositoryOutputLayout.PerEntity)]
    [InlineData("compatibility", RepositoryOutputLayout.SingleFile)]
    [InlineData("compatibility", RepositoryOutputLayout.PerEntity)]
    [InlineData("cachedCompatibility", RepositoryOutputLayout.SingleFile)]
    [InlineData("cachedCompatibility", RepositoryOutputLayout.PerEntity)]
    public void AllSupportedSurfacesAndLayoutsCompileWithoutWarnings(string surface, RepositoryOutputLayout layout)
    {
        using var context = MetadataContext();
        var builder = CoreBuilder(closed: surface == "closed");
        builder.Entity<PipelineRow>().HasQuery("NullableLimits").Select(row => row.Id).OrderBy(id => id)
            .SkipIf((ConditionalRequest request) => request.Offset.HasValue, limits => limits.Offset.GetValueOrDefault())
            .TakeIf(request => request.Limit.HasValue, (ConditionalRequest limits) => limits.Limit.GetValueOrDefault()).List();
        if (surface == "cqrs")
        {
            builder.Configure(options => options.UseCqrsSelectors());
        }

        if (surface == "cachedCompatibility")
        {
            builder.Configure(options => options.GenerateCacheDecorator());
            builder.Entity<PipelineRow>().HasQuery("Cached").Cache().Select(row => row.Name).OrderBy(name => name)
                .OrderByDescendingIf((ConditionalRequest request) => request.Rank, (name, criteria) => name.Length + criteria.Bias).TakeIf<ConditionalRequest>(request => request.Take, 2).List();
        }

        var files = RepositoryGenerator.Generate(Model(context, builder), layout).Files;
        files.Should().Equal(RepositoryGenerator.Generate(Model(context, builder), layout).Files);
        CompileFiles(files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts or RepositoryOutputKind.Projections).Select(file => file.Contents));
    }

    [Theory]
    [InlineData("OrderBy", false)]
    [InlineData("OrderBy", true)]
    [InlineData("OrderByDescending", false)]
    [InlineData("OrderByDescending", true)]
    [InlineData("ThenBy", false)]
    [InlineData("ThenBy", true)]
    [InlineData("ThenByDescending", false)]
    [InlineData("ThenByDescending", true)]
    public async Task BothSortSelectorFormsExecuteAndDisabledSortsAreAbsentFromSql(string operation, bool requestKey)
    {
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        var query = entity.HasQuery("Read").Results<PipelineRow>();
        var primary = operation.StartsWith("OrderBy", StringComparison.Ordinal);
        query.OrderBy(row => row.Id);
        if (operation == "OrderBy")
        {
            if (requestKey)
            {
                query.OrderByIf((ConditionalRequest request) => request.Rank, (row, criteria) => row.Amount + criteria.Bias);
            }
            else
            {
                query.OrderByIf((ConditionalRequest request) => request.Rank, row => row.Amount);
            }
        }
        else if (operation == "OrderByDescending")
        {
            if (requestKey)
            {
                query.OrderByDescendingIf((ConditionalRequest request) => request.Rank, (row, criteria) => row.Amount + criteria.Bias);
            }
            else
            {
                query.OrderByDescendingIf((ConditionalRequest request) => request.Rank, row => row.Amount);
            }
        }
        else if (operation == "ThenBy")
        {
            if (requestKey)
            {
                query.ThenByIf((ConditionalRequest request) => request.Rank, (row, criteria) => row.Amount + criteria.Bias);
            }
            else
            {
                query.ThenByIf((ConditionalRequest request) => request.Rank, row => row.Amount);
            }
        }
        else
        {
            if (requestKey)
            {
                query.ThenByDescendingIf((ConditionalRequest request) => request.Rank, (row, criteria) => row.Amount + criteria.Bias);
            }
            else
            {
                query.ThenByDescendingIf((ConditionalRequest request) => request.Rank, row => row.Amount);
            }
        }

        query.ThenBy(row => row.Id).Select(row => row.Id).List();
        await using var fixture = await Fixture.CreateAsync(builder);
        (await fixture.Call<Task<IReadOnlyList<int>>>("ReadAsync", new ConditionalRequest(), CancellationToken.None)).Should().Equal(1, 2, 3, 4, 5, 6);
        Ordering(fixture.Sql.Commands[^1].Text).Should().NotContain("Amount");
        fixture.Sql.Commands.Clear();
        var rows = await fixture.Call<Task<IReadOnlyList<int>>>("ReadAsync", new ConditionalRequest { Rank = true, Bias = 17 }, CancellationToken.None);
        if (!primary)
        {
            rows.Should().Equal(1, 2, 3, 4, 5, 6);
        }
        else if (operation == "OrderBy")
        {
            rows.Should().Equal(2, 1, 3, 4, 5, 6);
        }
        else
        {
            rows.Should().Equal(6, 5, 1, 3, 4, 2);
        }

        Ordering(fixture.Sql.Commands.Single().Text).Should().Contain("Amount");
        if (requestKey)
        {
            fixture.Sql.Commands.Single().Parameters.Values.Should().Contain(17);
        }

        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public void OptionalStagesDoNotChangeApiSignaturesAndContractsCompileIndependentlyOfEf()
    {
        using var context = MetadataContext();
        var builder = Builder();
        builder.Configure(options => options.IncludeApiDataContracts());
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Select(row => new ConditionalSummary { Id = row.Id, Amount = row.Amount, Name = row.Name })
            .OrderBy<int, ConditionalRequest>((row, request) => row.Id + request.Bias);
        query.List();
        var before = Model(context, builder);
        query.OrderByDescendingIf((ConditionalRequest request) => request.Rank, (row, ranking) => row.Amount + ranking.Bias)
            .ThenBy(row => row.Id).DistinctIf<ConditionalRequest>(request => request.Tie).OrderBy(row => row.Id)
            .SkipIf(request => request.Skip, (ConditionalRequest limits) => limits.Offset!.Value).List();
        var after = Model(context, builder);
        RepositoryApiSnapshot.Capture(after).ToJson().Should().Be(RepositoryApiSnapshot.Capture(before).ToJson());
        const string dtoSource = "namespace RonSijm.RepoGen.Tool.Tests { public sealed record ConditionalSummary { public int Id { get; init; } public int Amount { get; init; } public string Name { get; init; } = \"\"; } public sealed record ConditionalRequest; }";
        const string consumer = "class Consumer { public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<RonSijm.RepoGen.Tool.Tests.ConditionalSummary>> Read(Conditional.Contracts.IReadPipelineRowRepository repository, RonSijm.RepoGen.Tool.Tests.ConditionalRequest request) => repository.ReadAsync(request); }";
        var compilation = CSharpCompilation.Create("ConditionalContracts", [CSharpSyntaxTree.ParseText(ClosedRepositorySourceRenderer.RenderContracts(after)), CSharpSyntaxTree.ParseText(dtoSource), CSharpSyntaxTree.ParseText(consumer)],
            ClosedRepositoryTests.PlatformReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Should().BeEmpty();
    }

    [Fact]
    public async Task TypeAdaptersAreExpandedInConditionsSortKeysAndLimitExpressions()
    {
        var builder = Builder();
        var id = builder.HasTypeAdapter<int, ApplicationRowId>(value => new ApplicationRowId(value), value => value.Value);
        builder.Entity<PipelineRow>().HasQuery("Read").Results<PipelineRow>().OrderBy(row => row.Id)
            .OrderByDescendingIf((ConditionalAdaptedRequest request) => request.Rank, (row, preferences) => row.Amount + id.ToModel(preferences.Bias))
            .ThenBy(row => row.Id).TakeIf(request => id.ToModel(request.Limit) > 0, (ConditionalAdaptedRequest limits) => id.ToModel(limits.Limit))
            .Select(row => row.Id).List();
        await using var fixture = await Fixture.CreateAsync(builder);
        RepositorySourceRenderer.Render(Model(fixture.Context, builder)).Should().NotContain("ToModel(");
        (await fixture.Call<Task<IReadOnlyList<int>>>("ReadAsync", new ConditionalAdaptedRequest(false, new ApplicationRowId(17), new ApplicationRowId(0)), CancellationToken.None)).Should().HaveCount(6);
        (await fixture.Call<Task<IReadOnlyList<int>>>("ReadAsync", new ConditionalAdaptedRequest(true, new ApplicationRowId(17), new ApplicationRowId(2)), CancellationToken.None)).Should().Equal(6, 5);
    }

    [Fact]
    public async Task ConditionalDistinctOnlyRemovesDuplicatesOnTheEnabledBranch()
    {
        var builder = Builder();
        builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Category)
            .DistinctIf<ConditionalRequest>(request => request.Rank).OrderBy(category => category).List();
        await using var fixture = await Fixture.CreateAsync(builder);
        (await fixture.Call<Task<IReadOnlyList<string>>>("ReadAsync", new ConditionalRequest(), CancellationToken.None)).Should().Equal("A", "A", "A", "B", "B", "C");
        fixture.Sql.Commands.Single().Text.Should().NotContain("DISTINCT");
        fixture.Sql.Commands.Clear();
        (await fixture.Call<Task<IReadOnlyList<string>>>("ReadAsync", new ConditionalRequest { Rank = true }, CancellationToken.None)).Should().Equal("A", "B", "C");
        fixture.Sql.Commands.Single().Text.Should().Contain("SELECT DISTINCT");
    }

    [Fact]
    public void ConditionalDistinctCannotBypassOrderingOrNullStaleValidation()
    {
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Bad").Select(row => row.Id).OrderBy(id => id);
        Action invalid = () => query.DistinctIf<ConditionalRequest>(null!);
        invalid.Should().Throw<ArgumentNullException>();
        query.DistinctIf<ConditionalRequest>(request => request.Rank).RequirePagingOrder().Paged();
        using var context = MetadataContext();
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*RGD1501*");
        query.Select(id => id + 1L);
        Action stale = () => query.DistinctIf<ConditionalRequest>(request => request.Rank);
        stale.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
    }

    [Fact]
    public async Task IndependentRankingTieAndLimitBranchesExecuteWithoutAServiceQueryBuilder()
    {
        await using var fixture = await Fixture.CreateAsync(CoreBuilder());
        var defaultRows = await fixture.Call<Task<IReadOnlyList<ConditionalSummary>>>("ReadAsync", new ConditionalRequest(), CancellationToken.None);
        defaultRows.Select(row => row.Id).Should().Equal(1, 2, 3, 4, 5);
        Ordering(fixture.Sql.Commands.Single().Text).Should().NotContain("Amount").And.NotContain("Name");
        fixture.Sql.Commands.Single().Text.Should().NotContain("LIMIT");
        fixture.Sql.Commands.Clear();
        var ranked = new ConditionalRequest { Rank = true, Tie = true, Bias = 11 };
        (await fixture.Call<Task<IReadOnlyList<ConditionalSummary>>>("ReadAsync", ranked, CancellationToken.None)).Select(row => row.Id).Should().Equal(5, 1, 4, 3, 2);
        Ordering(fixture.Sql.Commands.Single().Text).Should().Contain("Amount").And.Contain("Name");
        fixture.Sql.Commands.Clear();
        var limited = await fixture.Call<Task<IReadOnlyList<ConditionalSummary>>>("ReadAsync", ranked with { Skip = true, Offset = 1, Take = true, Limit = 2 }, CancellationToken.None);
        limited.Select(row => row.Id).Should().Equal(1, 4);
        fixture.Sql.Commands.Single().Text.Should().Contain("LIMIT").And.Contain("OFFSET");
        fixture.Context.Database.GetCommandTimeout().Should().Be(17);
    }

    [Theory]
    [InlineData("Skip")]
    [InlineData("Take")]
    public async Task InactiveCountsAreNotEvaluatedAndEnabledNegativeCountsFailBeforeSql(string operation)
    {
        await using var fixture = await Fixture.CreateAsync(CoreBuilder());
        // Both nullable counts deliberately throw if evaluated. Disabled stages must not touch them.
        (await fixture.Call<Task<IReadOnlyList<ConditionalSummary>>>("ReadAsync", new ConditionalRequest(), CancellationToken.None)).Should().HaveCount(5);
        fixture.Sql.Commands.Clear();
        var request = operation == "Skip" ? new ConditionalRequest { Skip = true, Offset = -1 } : new ConditionalRequest { Take = true, Limit = -1 };
        Func<Task> execute = async () => await fixture.Call<Task<IReadOnlyList<ConditionalSummary>>>("ReadAsync", request, CancellationToken.None);
        await execute.Should().ThrowAsync<ArgumentOutOfRangeException>();
        fixture.Sql.Commands.Should().BeEmpty();
        fixture.Context.Database.GetCommandTimeout().Should().Be(17);
        request = operation == "Skip" ? request with { Offset = 0 } : request with { Limit = 0 };
        (await fixture.Call<Task<IReadOnlyList<ConditionalSummary>>>("ReadAsync", request, CancellationToken.None)).Count.Should().Be(operation == "Skip" ? 5 : 0);
    }

    [Fact]
    public async Task ConstantLimitsAreOptionalAndCountConditionsRunOncePerStage()
    {
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("Constant").Select(row => row.Id).OrderBy(id => id)
            .SkipIf<ConditionalRequest>(request => request.Skip, 1).TakeIf<ConditionalRequest>(request => request.Take, 2).List();
        entity.HasQuery("CountOnce").Select(row => row.Id).OrderBy(id => id)
            .SkipIf<ConditionalEvaluatedRequest>(request => request.Enabled, criteria => criteria.Offset)
            .TakeIf<ConditionalEvaluatedRequest>(request => request.Enabled, criteria => criteria.Limit).List();
        await using var fixture = await Fixture.CreateAsync(builder);
        (await fixture.Call<Task<IReadOnlyList<int>>>("ConstantAsync", new ConditionalRequest(), CancellationToken.None)).Should().HaveCount(6);
        (await fixture.Call<Task<IReadOnlyList<int>>>("ConstantAsync", new ConditionalRequest { Skip = true, Take = true }, CancellationToken.None)).Should().Equal(2, 3);
        var request = new ConditionalEvaluatedRequest();
        (await fixture.Call<Task<IReadOnlyList<int>>>("CountOnceAsync", request, CancellationToken.None)).Should().Equal(2, 3);
        request.Checks.Should().Be(2);
    }

    [Fact]
    public async Task InactiveUntranslatableRankingIsOmittedInsteadOfTriggeringClientFallback()
    {
        var builder = Builder();
        builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Name).OrderBy(name => name)
            .OrderByIf((ConditionalRequest request) => request.Rank, name => PipelineTransforms.Decorate(name)).List();
        await using var fixture = await Fixture.CreateAsync(builder);
        (await fixture.Call<Task<IReadOnlyList<string>>>("ReadAsync", new ConditionalRequest(), CancellationToken.None)).Should().HaveCount(6);
        fixture.Sql.Commands.Single().Text.Should().NotContain("Decorate");
        fixture.Sql.Commands.Clear();
        Func<Task> execute = async () => await fixture.Call<Task<IReadOnlyList<string>>>("ReadAsync", new ConditionalRequest { Rank = true }, CancellationToken.None);
        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("*translated*");
        fixture.Sql.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task CountedPagesSlicesAndGroupedJoinedRootsKeepServerSideOrderingAndCaps()
    {
        var builder = CoreBuilder();
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("Groups").Results<PipelineRow>().GroupBy(row => row.Category, group => new { Category = group.Key, Total = group.Sum(row => row.Amount) })
            .OrderBy(group => group.Category).OrderByDescendingIf((ConditionalRequest request) => request.Rank, group => group.Total)
            .ThenBy(group => group.Category).TakeIf<ConditionalRequest>(request => request.Take, 1).Select(group => group.Category).RequirePagingOrder().Paged();
        entity.HasQuery("Joined").From(sources => from left in sources.Set<PipelineRow>()
                                                  join right in sources.Set<PipelineRow>() on left.Id equals right.Id
                                                  select new { left.Id, left.Amount })
            .OrderBy(row => row.Id).OrderByDescendingIf((ConditionalRequest request) => request.Rank, row => row.Amount)
            .ThenBy(row => row.Id).TakeIf<ConditionalRequest>(request => request.Take, 2).Select(row => row.Id).List();
        await using var fixture = await Fixture.CreateAsync(builder);
        var request = new ConditionalRequest { Rank = true, Take = true, Limit = 2 };
        var page = await fixture.Call<Task<PagedResult<ConditionalSummary>>>("PageAsync", request, 1, 10, CancellationToken.None);
        page.TotalCount.Should().Be(2);
        page.Items.Select(row => row.Id).Should().Equal(5, 1);
        fixture.Sql.Commands.Should().HaveCount(2).And.OnlyContain(command => command.Transaction && command.Timeout == 3);
        fixture.Sql.Commands.Clear();
        var slice = await fixture.Call<Task<SliceResult<ConditionalSummary>>>("SliceAsync", request with { Take = false }, 1, 2, CancellationToken.None);
        slice.Items.Select(row => row.Id).Should().Equal(5, 1);
        slice.HasMore.Should().BeTrue();
        fixture.Sql.Commands.Should().ContainSingle();
        fixture.Sql.Commands.Clear();
        (await fixture.Call<Task<PagedResult<string>>>("GroupsAsync", request, 1, 10, CancellationToken.None)).Items.Should().Equal("A");
        fixture.Sql.Commands.Should().HaveCount(2);
        fixture.Sql.Commands[^1].Text.Should().Contain("GROUP BY").And.Contain("ORDER BY").And.Contain("LIMIT");
        fixture.Sql.Commands.Clear();
        (await fixture.Call<Task<IReadOnlyList<int>>>("JoinedAsync", request, CancellationToken.None)).Should().Equal(5, 1);
        fixture.Sql.Commands.Single().Text.Should().Contain("INNER JOIN").And.Contain("LIMIT");
    }

    [Fact]
    public async Task ConditionalCursorTieBreakersAndDictionaryTerminalsUseTheSelectedPipeline()
    {
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("Cursor").Select(row => new ConditionalSummary { Id = row.Id, Amount = row.Amount, Name = row.Name }).OrderBy(row => row.Id)
            .ThenByDescendingIf((ConditionalRequest request) => request.Tie, row => row.Amount).CursorPaged<int>((row, cursor) => row.Id > cursor, row => row.Id);
        entity.HasQuery("Dictionary").Select(row => new ConditionalSummary { Id = row.Id, Amount = row.Amount, Name = row.Name }).OrderBy(row => row.Id)
            .TakeIf<ConditionalRequest>(request => request.Take, 2).Dictionary(row => row.Id, row => row.Name);
        await using var fixture = await Fixture.CreateAsync(builder);
        var request = new ConditionalRequest { Tie = true, Take = true };
        var first = await fixture.Call<Task<CursorPagedResult<ConditionalSummary, int>>>("CursorAsync", request, new CursorPageRequest<int>(2), CancellationToken.None);
        first.Items.Select(row => row.Id).Should().Equal(1, 2);
        first.NextCursor.Should().Be(2);
        var next = await fixture.Call<Task<CursorPagedResult<ConditionalSummary, int>>>("CursorAsync", request, new CursorPageRequest<int>(first.NextCursor, 2), CancellationToken.None);
        next.Items.Select(row => row.Id).Should().Equal(3, 4);
        (await fixture.Call<Task<IReadOnlyDictionary<int, string>>>("DictionaryAsync", request, CancellationToken.None)).Keys.Should().Equal(1, 2);
    }

    [Fact]
    public async Task RegisteredRootsAndCommandSideReadsSupportOptionalStagesInsideSharedWork()
    {
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases());
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("Registered").FromProvider<ConditionalQueryRoot, ConditionalRequest, ConditionalSummary>(request => request)
            .OrderBy(row => row.Id).OrderByDescendingIf((ConditionalRequest request) => request.Rank, row => row.Amount).ThenBy(row => row.Id)
            .TakeIf<ConditionalRequest>(request => request.Take, 2).RequirePagingOrder().Paged();
        entity.HasQuery("LoadForWrite").OnWriteRepository().Results<PipelineRow>().OrderBy(row => row.Id)
            .OrderByDescendingIf((ConditionalRequest request) => request.Rank, row => row.Amount).ThenBy(row => row.Id)
            .TakeIf<ConditionalRequest>(request => request.Take, 2).Select(row => row.Id).List();
        await using var fixture = await Fixture.CreateAsync(builder, compile: false);
        var root = new ConditionalQueryRoot();
        await using var work = new RepositoryUnitOfWork<PipelineContext>(fixture.Context);
        var assembly = ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true);
        var operations = Activator.CreateInstance(assembly.GetType(typeof(PipelineRow).Namespace + ".PipelineRowRepositoryOperations")!, work, root)!;
        await using var transaction = await work.BeginTransactionAsync();
        var request = new ConditionalRequest { Rank = true, Take = true };
        var page = await Call<Task<PagedResult<ConditionalSummary>>>(operations, "RegisteredAsync", request, 1, 10, CancellationToken.None);
        page.Items.Select(row => row.Id).Should().Equal(6, 5);
        root.SawTransaction.Should().BeTrue();
        root.Timeout.Should().Be(3);
        (await Call<Task<IReadOnlyList<int>>>(operations, "LoadForWriteAsync", request, CancellationToken.None)).Should().Equal(6, 5);
        await transaction.RollbackAsync();
        fixture.Context.Database.GetCommandTimeout().Should().Be(17);
    }

    [Fact]
    public async Task StreamingAndFailedLimitsDisposeOwnedLeasesWithoutAcquiringEarly()
    {
        var builder = CoreBuilder();
        builder.Configure(options => options.UseAsyncContextLeases());
        await using var fixture = await Fixture.CreateAsync(builder, compile: false);
        var provider = new ConditionalLeaseProvider(fixture.Options);
        var assembly = ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true);
        var operations = Activator.CreateInstance(assembly.GetType(typeof(PipelineRow).Namespace + ".PipelineRowRepositoryOperations")!, provider)!;
        var stream = Call<IAsyncEnumerable<ConditionalSummary>>(operations, "StreamAsync", new ConditionalRequest { Rank = true }, CancellationToken.None);
        provider.Contexts.Should().BeEmpty();
        var enumerator = stream.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(0);
        await enumerator.DisposeAsync();
        provider.Contexts[0].DisposeCount.Should().Be(1);
        Func<Task> fail = async () => await Call<Task<IReadOnlyList<ConditionalSummary>>>(operations, "ReadAsync", new ConditionalRequest { Skip = true, Offset = -1 }, CancellationToken.None);
        await fail.Should().ThrowAsync<ArgumentOutOfRangeException>();
        provider.Contexts.Should().HaveCount(2).And.OnlyContain(context => context.DisposeCount == 1);
        using var canceled = new CancellationTokenSource();
        var cancellation = Call<IAsyncEnumerable<ConditionalSummary>>(operations, "StreamAsync", new ConditionalRequest(), canceled.Token).GetAsyncEnumerator();
        (await cancellation.MoveNextAsync()).Should().BeTrue();
        canceled.Cancel();
        Func<Task> advance = async () => await cancellation.MoveNextAsync();
        await advance.Should().ThrowAsync<OperationCanceledException>();
        await cancellation.DisposeAsync();
        provider.Contexts.Should().HaveCount(3).And.OnlyContain(context => context.DisposeCount == 1);
    }

    [Fact]
    public async Task CachedBranchesHaveSeparateKeysAndUnusedNavigationSortsRemainDependencies()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryRepositoryResultCache(memory);
        var builder = CoreBuilder();
        builder.Configure(options => options.UseClosedCaching("conditional-fixture"));
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("Cached").Cache().Select(row => new ConditionalSummary { Id = row.Id, Amount = row.Amount, Name = row.Name }).OrderBy(row => row.Id)
            .OrderByDescendingIf((ConditionalRequest request) => request.Rank, row => row.Amount).ThenBy(row => row.Id)
            .TakeIf(request => request.Take, (ConditionalRequest criteria) => criteria.Limit!.Value).List();
        await using var fixture = await Fixture.CreateAsync(builder, compile: false);
        var operations = Activator.CreateInstance(ClosedRepositoryTests.Compile(Model(fixture.Context, builder)).GetType(typeof(PipelineRow).Namespace + ".PipelineRowRepositoryOperations")!, fixture.Context, cache)!;
        var plain = new ConditionalRequest();
        var ranked = plain with { Rank = true };
        (await Call<Task<IReadOnlyList<ConditionalSummary>>>(operations, "CachedAsync", plain, CancellationToken.None)).Select(row => row.Id).Should().Equal(1, 2, 3, 4, 5);
        (await Call<Task<IReadOnlyList<ConditionalSummary>>>(operations, "CachedAsync", ranked, CancellationToken.None)).Select(row => row.Id).Should().Equal(5, 1, 3, 4, 2);
        (await Call<Task<IReadOnlyList<ConditionalSummary>>>(operations, "CachedAsync", ranked with { Take = true, Limit = 2 }, CancellationToken.None)).Should().HaveCount(2);
        fixture.Sql.Commands.Should().HaveCount(3);
        fixture.Sql.Commands.Clear();
        (await Call<Task<IReadOnlyList<ConditionalSummary>>>(operations, "CachedAsync", plain, CancellationToken.None)).Should().HaveCount(5);
        fixture.Sql.Commands.Should().BeEmpty();
        await using var commandContext = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var dependencies = Builder();
        dependencies.Entity<CommandRow>().HasQuery("Read").Results<CommandRow>().OrderBy(row => row.Id)
            .OrderByIf((ConditionalRequest request) => request.Rank, row => row.Owner == null ? 0 : row.Owner.Id).Select(row => row.Name).List();
        RepositoryModelFactory.Create(commandContext, typeof(CommandRow).Assembly, design: dependencies.Build()).Entities.Single().Queries.Single()
            .Cache.DependentEntityTypeNames.Should().Contain(RepositoryModelFactory.CSharpTypeName(typeof(CommandOwner)));
    }

    private static RepositoryModelBuilder Builder(bool closed = true)
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        if (closed)
        {
            builder.Configure(options => options.UseClosedRepositories("Conditional.Contracts").UseCommandTimeout(3));
        }

        return builder;
    }

    private static RepositoryModelBuilder CoreBuilder(bool closed = true)
    {
        var builder = Builder(closed);
        var entity = builder.Entity<PipelineRow>();
        entity.HasVisibilityPolicy("Visible", row => row.Amount <= 30);
        foreach (var operation in new[] { "Read", "Page", "Slice", "Stream" })
        {
            var query = entity.HasQuery(operation).UseDefaultSort(false).Select(row => new ConditionalSummary { Id = row.Id, Amount = row.Amount, Name = row.Name }).OrderBy(row => row.Id)
                .OrderByDescendingIf((ConditionalRequest request) => request.Rank, (row, criteria) => row.Amount + criteria.Bias)
                .ThenByDescendingIf((ConditionalRequest request) => request.Tie, row => row.Name).ThenBy(row => row.Id)
                .SkipIf(request => request.Skip, (ConditionalRequest count) => ConditionalCounts.Offset(count))
                .TakeIf(request => request.Take, (ConditionalRequest count) => ConditionalCounts.Limit(count));
            if (operation == "Read")
            {
                query.List();
            }
            else if (operation == "Stream")
            {
                query.Stream();
            }
            else if (operation == "Slice")
            {
                query.RequirePagingOrder().Slice();
            }
            else
            {
                query.RequirePagingOrder().Paged(RepositoryPageConsistency.Serializable);
            }
        }

        return builder;
    }

    private static string Ordering(string sql) => sql[(sql.IndexOf("ORDER BY", StringComparison.Ordinal) + "ORDER BY".Length)..];
    private static PipelineContext MetadataContext() => new(new DbContextOptionsBuilder<PipelineContext>().UseSqlite("Data Source=:memory:").Options);
    private static RepositoryGenerationModel Model(PipelineContext context, RepositoryModelBuilder builder) => RepositoryModelFactory.Create(context, typeof(PipelineRow).Assembly, design: builder.Build());
    private static T Call<T>(object operations, string method, params object[] arguments) => (T)operations.GetType().GetMethod(method)!.Invoke(operations, arguments)!;

    private static void CompileFiles(IEnumerable<string> sources)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(ConditionalQueryStageTests).Assembly.Location).Append(typeof(DbContext).Assembly.Location)
            .Append(typeof(RelationalQueryableExtensions).Assembly.Location).Append(typeof(IDbContextLeaseProvider<>).Assembly.Location)
            .Append(typeof(PagedResult<>).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create("Conditional_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            paths.Select(path => MetadataReference.CreateFromFile(path)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        result.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error || item.Severity == DiagnosticSeverity.Warning && item.Id != "CS1701").Should().BeEmpty();
        output.Position = 0;
        AssemblyLoadContext.Default.LoadFromStream(output).Should().NotBeNull();
    }

    private sealed class Fixture(SqliteConnection connection, PipelineContext context, DbContextOptions<PipelineContext> options, Recorder sql, object? operations) : IAsyncDisposable
    {
        public PipelineContext Context { get; } = context;
        public DbContextOptions<PipelineContext> Options { get; } = options;
        public Recorder Sql { get; } = sql;
        public T Call<T>(string method, params object[] arguments) => ConditionalQueryStageTests.Call<T>(operations!, method, arguments);

        public static async Task<Fixture> CreateAsync(RepositoryModelBuilder builder, bool compile = true)
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
            var operations = compile ? Activator.CreateInstance(ClosedRepositoryTests.Compile(Model(context, builder), rejectWarnings: true).GetType(typeof(PipelineRow).Namespace + ".PipelineRowRepositoryOperations")!, context) : null;
            return new Fixture(connection, context, options, sql, operations);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class Recorder : DbCommandInterceptor
    {
        public List<(string Text, IReadOnlyDictionary<string, object?> Parameters, bool Transaction, int Timeout)> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add((command.CommandText, command.Parameters.Cast<DbParameter>().ToDictionary(parameter => parameter.ParameterName, parameter => (object?)parameter.Value), command.Transaction is not null, command.CommandTimeout));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ConditionalLeaseProvider(DbContextOptions<PipelineContext> options) : IDbContextLeaseProvider<PipelineContext>
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

public sealed record ConditionalSummary
{
    public int Id { get; init; }
    public int Amount { get; init; }
    public string Name { get; init; } = "";
}

public sealed record ConditionalRequest
{
    public bool Rank { get; init; }
    public bool Tie { get; init; }
    public bool Skip { get; init; }
    public bool Take { get; init; }
    public int? Offset { get; init; }
    public int? Limit { get; init; }
    public int Bias { get; init; }
}

public sealed record ConditionalAdaptedRequest(bool Rank, ApplicationRowId Bias, ApplicationRowId Limit);

public sealed class ConditionalEvaluatedRequest
{
    public int Checks { get; private set; }

    public bool Enabled
    {
        get
        {
            Checks++;
            return true;
        }
    }

    public int Offset { get; init; } = 1;
    public int Limit { get; init; } = 2;
}

public static class ConditionalCounts
{
    public static int Offset(ConditionalRequest request) => request.Offset ?? throw new InvalidOperationException("Offset must not be evaluated on a disabled branch.");
    public static int Limit(ConditionalRequest request) => request.Limit ?? throw new InvalidOperationException("Limit must not be evaluated on a disabled branch.");
}

public sealed class ConditionalQueryRoot : IRepositoryQueryRoot<PipelineContext, ConditionalRequest, ConditionalSummary>
{
    public bool SawTransaction { get; private set; }
    public int? Timeout { get; private set; }

    public IQueryable<ConditionalSummary> CreateQuery(PipelineContext context, ConditionalRequest request)
    {
        SawTransaction = context.Database.CurrentTransaction is not null;
        Timeout = context.Database.GetCommandTimeout();
        return context.Rows.AsNoTracking().Select(row => new ConditionalSummary { Id = row.Id, Amount = row.Amount, Name = row.Name });
    }
}