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

public sealed class RequestCursorTests
{
    [Theory]
    [InlineData(false, "seek")]
    [InlineData(false, "mapper")]
    [InlineData(false, "guard")]
    [InlineData(true, "seek")]
    [InlineData(true, "mapper")]
    [InlineData(true, "guard")]
    public void NullExpressionsDoNotReplaceTheExistingTerminal(bool results, string scenario)
    {
        var builder = Builder();
        var root = builder.Entity<PipelineRow>().HasQuery("Read");
        var query = results ? root.Select(row => row.Id).List() : null;
        Action configure = () =>
        {
            if (query is not null)
            {
                if (scenario == "seek")
                {
                    query.CursorPaged<ReportCursor, CursorReportRequest>((Expression<Func<int, ReportCursor, CursorReportRequest, bool>>)null!, (id, request) => new ReportCursor(id, request.Descending, request.Scope));
                }
                else if (scenario == "mapper")
                {
                    query.CursorPaged<ReportCursor, CursorReportRequest>((id, cursor, request) => id > cursor.Id, (Expression<Func<int, CursorReportRequest, ReportCursor>>)null!);
                }
                else
                {
                    query.ValidateCursor<ReportCursor, CursorReportRequest>(null!, "Scope mismatch.");
                }
            }
            else
            {
                if (scenario == "seek")
                {
                    root.CursorPaged<ReportCursor, CursorReportRequest>((Expression<Func<PipelineRow, ReportCursor, CursorReportRequest, bool>>)null!, (row, request) => new ReportCursor(row.Id, request.Descending, request.Scope));
                }
                else if (scenario == "mapper")
                {
                    root.CursorPaged<ReportCursor, CursorReportRequest>((row, cursor, request) => row.Id > cursor.Id, (Expression<Func<PipelineRow, CursorReportRequest, ReportCursor>>)null!);
                }
                else
                {
                    root.ValidateCursor<ReportCursor, CursorReportRequest>(null!, "Scope mismatch.");
                }
            }
        };
        configure.Should().Throw<ArgumentNullException>();
        var design = builder.Build().Entities.Single().Queries.Single();
        design.ResultKind.Should().Be(RepositoryQueryResultKind.List);
        design.Cursor.Should().BeNull();
        design.CursorValidations.Should().BeEmpty();
    }

    [Fact]
    public void InvalidGuardsAndStaleBuildersDoNotMutateTheDesign()
    {
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id);
        Action invalid = () => query.ValidateCursor<ReportCursor, CursorReportRequest>((cursor, request) => true, " ");
        invalid.Should().Throw<ArgumentException>();
        var latest = query.Select(id => id + 1).List();
        Action staleCursor = () => query.CursorPaged<int, CursorReportRequest>((id, cursor, request) => id > cursor, id => id);
        Action staleGuard = () => query.ValidateCursor<int, CursorReportRequest>((cursor, request) => true, "Mismatch.");
        staleCursor.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        staleGuard.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        latest.List();
        builder.Build().Entities.Single().Queries.Single().CursorValidations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PairedMappingsShareOneRequestAndTheOptionalManifestIsReviewable(bool results)
    {
        using var context = MetadataContext();
        var builder = Builder();
        var root = builder.Entity<PipelineRow>().HasQuery("Read").ProjectTo(row => row.Id);
        if (results)
        {
            root.Results<int>().OrderBy(id => id)
                .CursorPaged<ReportCursor, CursorReportRequest>((id, cursor, request) => request.Descending ? id < cursor.Id : id > cursor.Id,
                    (id, formatting) => new ReportCursor(id, formatting.Descending, formatting.Scope))
                .ValidateCursor<ReportCursor, CursorReportRequest>((cursor, request) => cursor.Descending == request.Descending, "Direction mismatch.");
        }
        else
        {
            root.OrderBy(row => row.Id)
                .CursorPaged<ReportCursor, CursorReportRequest>((row, cursor, request) => request.Descending ? row.Id < cursor.Id : row.Id > cursor.Id,
                    (row, formatting) => new ReportCursor(row.Id, formatting.Descending, formatting.Scope))
                .ValidateCursor<ReportCursor, CursorReportRequest>((cursor, request) => cursor.Descending == request.Descending, "Direction mismatch.");
        }

        var design = builder.Build().Entities.Single().Queries.Single();
        design.Cursor!.CursorSelector.Parameters[1].Should().BeSameAs(design.Cursor.SeekPredicate.Parameters[2]);
        var model = Model(context, builder);
        var configured = model.Entities.Single().Queries.Single();
        configured.Parameters.Should().ContainSingle().Which.Name.Should().Be("request");
        configured.Cursor!.CursorSelectorExpressionSource.Should().Contain("request.Scope").And.NotContain("formatting");
        configured.ApiDataTypes.Should().Contain(typeof(CursorReportRequest)).And.Contain(typeof(ReportCursor));
        var inventory = JsonNode.Parse(RepositoryManifestRenderer.Render(model))!["entities"]![0]!["queries"]![0]!;
        inventory["cursorRequest"]!["parameter"]!.GetValue<string>().Should().Be("request");
        inventory["cursorValidations"]!.AsArray().Should().ContainSingle();
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
    public void EverySurfaceAndLayoutCompilesTheIndependentSeekAndMapperShapes(string surface, RepositoryOutputLayout layout)
    {
        using var context = MetadataContext();
        var builder = Builder(surface);
        var entity = builder.Entity<PipelineRow>();
        var root = entity.HasQuery("EntityBoth");
        if (surface == "cachedSelectors")
        {
            root.Cache();
        }

        Configure(root, results: false);
        Configure(entity.HasQuery("ResultBoth"), results: true);
        entity.HasQuery("EntitySeek").ProjectTo(row => row.Id).OrderBy(row => row.Id)
            .CursorPaged<int, CursorReportRequest>((row, cursor, request) => row.Id > cursor + request.Offset, row => row.Id);
        entity.HasQuery("EntityMapper").ProjectTo(row => row.Id).OrderBy(row => row.Id)
            .CursorPaged<ReportCursor, CursorReportRequest>((row, cursor) => row.Id > cursor.Id, (row, request) => new ReportCursor(row.Id, request.Descending, request.Scope));
        entity.HasQuery("ResultSeek").Select(row => row.Id).OrderBy(id => id)
            .CursorPaged<int, CursorReportRequest>((id, cursor, request) => id > cursor + request.Offset, id => id);
        entity.HasQuery("ResultMapper").Select(row => row.Id).OrderBy(id => id)
            .CursorPaged<ReportCursor, CursorReportRequest>((id, cursor) => id > cursor.Id, (id, request) => new ReportCursor(id, request.Descending, request.Scope));
        entity.HasQuery("GuardOnly").Select(row => row.Id).OrderBy(id => id).CursorPaged<int>((id, cursor) => id > cursor, id => id)
            .ValidateCursor<int, CursorReportRequest>((cursor, request) => cursor >= request.Offset, "Cursor precedes the allowed range.");
        var model = Model(context, builder);
        var files = RepositoryGenerator.Generate(model, layout).Files;
        files.Should().Equal(RepositoryGenerator.Generate(model, layout).Files);
        CompileFiles(files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts or RepositoryOutputKind.Projections).Select(file => file.Contents));
    }

    [Theory]
    [InlineData("closed", false, false)]
    [InlineData("closed", false, true)]
    [InlineData("closed", true, false)]
    [InlineData("closed", true, true)]
    [InlineData("cqrs", false, false)]
    [InlineData("cqrs", false, true)]
    [InlineData("cqrs", true, false)]
    [InlineData("cqrs", true, true)]
    [InlineData("selectors", false, false)]
    [InlineData("selectors", false, true)]
    [InlineData("selectors", true, false)]
    [InlineData("selectors", true, true)]
    public async Task RequestsControlBothDirectionAndContinuationWithoutCountsOrClientFiltering(string surface, bool results, bool descending)
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder(surface);
        var entity = builder.Entity<PipelineRow>();
        entity.HasVisibilityPolicy("Visible", row => row.Id < 6);
        Configure(entity.HasQuery("Read"), results);
        var operations = Create(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), fixture.Context, surface);
        var request = new CursorReportRequest(descending, "A");
        var first = await Page(operations, "ReadAsync", request, 2);
        first.Items.Should().Equal(descending ? 5 : 1, 3);
        first.HasMore.Should().BeTrue();
        first.Cursor.Should().Be(new ReportCursor(3, descending, "A"));
        var second = await Page(operations, "ReadAsync", request, 2, first.Cursor);
        second.Items.Should().Equal(descending ? 1 : 5);
        second.HasMore.Should().BeFalse();
        fixture.Sql.Commands.Should().HaveCount(2).And.OnlyContain(command => command.Text.Contains("ORDER BY") && command.Text.Contains("LIMIT") && !command.Text.Contains("COUNT("));
        fixture.Sql.Commands[1].Text.Should().Contain(descending ? "<" : ">");
        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
        fixture.Context.Database.GetCommandTimeout().Should().Be(17);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CursorGuardsNullPagesAndCancellationRejectBeforeOwnedAcquisition(bool results)
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases().UseCommandTimeout(3));
        Configure(builder.Entity<PipelineRow>().HasQuery("Read"), results);
        var provider = new OwnedProvider(fixture.Options);
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "PipelineRowRepositoryOperations"), provider)!;
        var request = new CursorReportRequest(false, "A");
        Func<Task> invalid = async () => await Page(operations, "ReadAsync", request, 2, new ReportCursor(1, true, "A"));
        await invalid.Should().ThrowAsync<ArgumentException>().Where(exception => exception.ParamName == "cursorPage");
        Func<Task> otherScope = async () => await Page(operations, "ReadAsync", request, 2, new ReportCursor(1, false, "B"));
        await otherScope.Should().ThrowAsync<ArgumentException>();
        Func<Task> nullPage = async () => await Invoke(operations, "ReadAsync", request, null!, CancellationToken.None);
        await nullPage.Should().ThrowAsync<ArgumentNullException>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Func<Task> canceled = async () => await Page(operations, "ReadAsync", request, 2, cancellation: cancellation.Token);
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        provider.Contexts.Should().BeEmpty();
        fixture.Sql.Commands.Should().BeEmpty();
        var page = await Page(operations, "ReadAsync", request, 2);
        page.Items.Should().Equal(1, 3);
        provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
        fixture.Sql.Commands.Should().ContainSingle().Which.Timeout.Should().Be(3);
    }

    [Fact]
    public async Task TheFirstPageDoesNotEvaluateCursorGuardsAndCanceledGuardsNeverAcquire()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases());
        builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id).OrderBy(id => id).CursorPaged<int>((id, cursor) => id > cursor, id => id)
            .ValidateCursor<int, CursorGuardRequest>((cursor, request) => request.IsValid, "Invalid cursor.");
        var provider = new OwnedProvider(fixture.Options);
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "PipelineRowRepositoryOperations"), provider)!;
        using var cancellation = new CancellationTokenSource();
        var request = new CursorGuardRequest(cancellation);
        var first = await Invoke(operations, "ReadAsync", request, new CursorPageRequest<int>(2), cancellation.Token);
        first.Should().NotBeNull();
        request.Reads.Should().Be(0);
        provider.Contexts.Should().HaveCount(1);
        Func<Task> canceled = async () => await Invoke(operations, "ReadAsync", request, new CursorPageRequest<int>(2, 2), cancellation.Token);
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        request.Reads.Should().Be(1);
        provider.Contexts.Should().HaveCount(1);
    }

    [Fact]
    public async Task GuardsRunOnCacheHitsAndKeysIncludeBothCursorAndRequest()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var builder = Builder();
        builder.Configure(options => options.UseClosedCaching("cursor-fixture"));
        Configure(builder.Entity<PipelineRow>().HasQuery("Read").Cache(), results: true);
        var cache = new CountingCache(new MemoryRepositoryResultCache(memory));
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "PipelineRowRepositoryOperations"), fixture.Context, cache)!;
        var request = new CursorReportRequest(false, "A");
        var first = await Page(operations, "ReadAsync", request, 2);
        (await Page(operations, "ReadAsync", request with { }, 2)).Items.Should().Equal(first.Items);
        (await Page(operations, "ReadAsync", request, 2, first.Cursor)).Items.Should().Equal(5);
        (await Page(operations, "ReadAsync", request with { Descending = true }, 2)).Items.Should().Equal(5, 3);
        var reads = cache.Reads;
        Func<Task> invalid = async () => await Page(operations, "ReadAsync", request with { Descending = true }, 2, first.Cursor);
        await invalid.Should().ThrowAsync<ArgumentException>();
        cache.Reads.Should().Be(reads);
        fixture.Sql.Commands.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("seek")]
    [InlineData("mapper")]
    [InlineData("guard")]
    public void CursorOnlyRequestsCannotExposeEfThroughClosedContracts(string scenario)
    {
        using var context = MetadataContext();
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id).OrderBy(id => id);
        if (scenario == "seek")
        {
            query.CursorPaged<int, TerminalEfRequest>((id, cursor, request) => id > cursor, id => id);
        }
        else if (scenario == "mapper")
        {
            query.CursorPaged<int, TerminalEfRequest>((id, cursor) => id > cursor, (id, request) => id);
        }
        else
        {
            query.CursorPaged<int>((id, cursor) => id > cursor, id => id).ValidateCursor<int, TerminalEfRequest>((cursor, request) => true, "Mismatch.");
        }

        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1502:*request*");
    }

    [Theory]
    [InlineData("withoutCursor")]
    [InlineData("wrongType")]
    [InlineData("replacedTerminal")]
    public void CursorGuardsRequireTheFinalMatchingCursorOperation(string scenario)
    {
        using var context = MetadataContext();
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id).OrderBy(id => id);
        query.ValidateCursor<ReportCursor, CursorReportRequest>((cursor, request) => true, "Mismatch.");
        if (scenario == "wrongType")
        {
            query.CursorPaged<int>((id, cursor) => id > cursor, id => id);
        }

        if (scenario == "replacedTerminal")
        {
            query.CursorPaged<ReportCursor, CursorReportRequest>((id, cursor, request) => id > cursor.Id,
                (id, request) => new ReportCursor(id, false, null)).Count();
        }

        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*RGD2702*");
    }

    [Fact]
    public void EntityRequestCursorsRejectReplacementRootsAndNewCursorsRequireOrdering()
    {
        using var context = MetadataContext();
        var builder = Builder();
        var root = builder.Entity<PipelineRow>().HasQuery("Read");
        root.Select(row => row.Id);
        Action invalid = () => root.CursorPaged<int, CursorReportRequest>((row, cursor, request) => row.Id > cursor, row => row.Id);
        invalid.Should().Throw<InvalidOperationException>().WithMessage("*RGD2701*");
        builder.Entity<PipelineRow>().ExcludeQuery("Read");
        builder.Entity<PipelineRow>().HasQuery("Unordered").ProjectTo(row => row.Id)
            .CursorPaged<int, CursorReportRequest>((row, cursor, request) => row.Id > cursor, row => row.Id);
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*RGD1501*");
    }

    [Fact]
    public void UnusedCursorRequestsAndExcludedQueriesDoNotChangeExistingGeneratedOutputs()
    {
        using var context = MetadataContext();
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("Read").Count();
        var original = RepositoryGenerator.Generate(Model(context, builder)).Files;
        entity.HasQuery("Unused").Select(row => row.Id).OrderBy(id => id).CursorPaged<int, TerminalEfRequest>((id, cursor, request) => id > cursor, id => id);
        entity.ExcludeQuery("Unused");
        RepositoryGenerator.Generate(Model(context, builder)).Files.Should().Equal(original);
        var query = entity.HasQuery("Replaced").Select(row => row.Id).OrderBy(id => id)
            .CursorPaged<int, CursorReportRequest>((id, cursor, request) => id > cursor, id => id);
        query.List();
        var model = Model(context, builder);
        var read = model.Entities.Single().Queries.Single(query => query.MethodName == "ReplacedAsync");
        read.Cursor.Should().BeNull();
        read.Parameters.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CursorOnlyGuardsNeedNoExtraRequestAndRejectBeforeSql(bool results)
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        var root = builder.Entity<PipelineRow>().HasQuery("Read").ProjectTo(row => row.Id);
        if (results)
        {
            root.Results<int>().OrderBy(id => id).CursorPaged<int>((id, cursor) => id > cursor, id => id).ValidateCursor<int>(cursor => cursor > 0, "Positive cursor required.");
        }
        else
        {
            root.OrderBy(row => row.Id).CursorPaged<int>((row, cursor) => row.Id > cursor, row => row.Id).ValidateCursor<int>(cursor => cursor > 0, "Positive cursor required.");
        }

        var model = Model(fixture.Context, builder);
        model.Entities.Single().Queries.Single().Parameters.Should().BeEmpty();
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(model, rejectWarnings: true), "PipelineRowRepositoryOperations"), fixture.Context)!;
        var first = (CursorPagedResult<int, int>)await Invoke(operations, "ReadAsync", new CursorPageRequest<int>(2), CancellationToken.None);
        first.Items.Should().Equal(1, 2);
        Func<Task> invalid = async () => await Invoke(operations, "ReadAsync", new CursorPageRequest<int>(0, 2), CancellationToken.None);
        await invalid.Should().ThrowAsync<ArgumentException>().Where(exception => exception.ParamName == "cursorPage");
        fixture.Sql.Commands.Should().ContainSingle();
    }

    [Fact]
    public async Task IndependentSeekAndMapperRequestsExecuteOnEntityAndResultCursors()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("EntitySeek").ProjectTo(row => row.Id).OrderBy(row => row.Id)
            .CursorPaged<int, CursorReportRequest>((row, cursor, request) => row.Id > cursor + request.Offset, row => row.Id);
        entity.HasQuery("ResultSeek").Select(row => row.Id).OrderBy(id => id)
            .CursorPaged<int, CursorReportRequest>((id, cursor, request) => id > cursor + request.Offset, id => id);
        entity.HasQuery("EntityMapper").ProjectTo(row => row.Id).OrderBy(row => row.Id)
            .CursorPaged<ReportCursor, CursorReportRequest>((row, cursor) => row.Id > cursor.Id, (row, request) => new ReportCursor(row.Id, request.Descending, request.Scope));
        entity.HasQuery("ResultMapper").Select(row => row.Id).OrderBy(id => id)
            .CursorPaged<ReportCursor, CursorReportRequest>((id, cursor) => id > cursor.Id, (id, request) => new ReportCursor(id, request.Descending, request.Scope));
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "PipelineRowRepositoryOperations"), fixture.Context)!;
        var request = new CursorReportRequest(false, "metadata", 1);
        foreach (var method in new[] { "EntitySeekAsync", "ResultSeekAsync" })
        {
            var page = (CursorPagedResult<int, int>)await Invoke(operations, method, request, new CursorPageRequest<int>(2, 2), CancellationToken.None);
            page.Items.Should().Equal(4, 5);
            page.NextCursor.Should().Be(5);
        }

        foreach (var method in new[] { "EntityMapperAsync", "ResultMapperAsync" })
        {
            var page = await Page(operations, method, request, 2, new ReportCursor(2, false, "metadata"));
            page.Items.Should().Equal(3, 4);
            page.Cursor.Should().Be(new ReportCursor(4, false, "metadata"));
        }

        fixture.Sql.Commands.Should().HaveCount(4).And.OnlyContain(command => command.Text.Contains("WHERE") && command.Text.Contains("LIMIT"));
    }

    [Fact]
    public async Task GroupedCompositeCursorsRemainServerFilteredAndKeepTheirRequestMetadata()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        entity.HasVisibilityPolicy("Visible", row => row.Id < 6);
        entity.HasQuery("Read").Results<PipelineRow>().GroupBy(row => row.Category, group => new { Category = group.Key, Total = group.Sum(row => row.Amount), FirstId = group.Min(row => row.Id) })
            .OrderBy(group => group.Total).ThenBy(group => group.FirstId)
            .Select(group => new CursorGroupRow { Category = group.Category, Total = group.Total, FirstId = group.FirstId })
            .CursorPaged<GroupReportCursor, CursorReportRequest>((group, cursor, request) => group.Total > cursor.Total ||
                                                                                             group.Total == cursor.Total && group.FirstId > cursor.FirstId,
                (group, formatting) => new GroupReportCursor(group.Total, group.FirstId, formatting.Offset))
            .ValidateCursor<GroupReportCursor, CursorReportRequest>((cursor, request) => cursor.Offset == request.Offset, "Report scope changed.");
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), "PipelineRowRepositoryOperations"), fixture.Context)!;
        var request = new CursorReportRequest(false, null, 10);
        var first = (CursorPagedResult<CursorGroupRow, GroupReportCursor>)await Invoke(operations, "ReadAsync", request, new CursorPageRequest<GroupReportCursor>(1), CancellationToken.None);
        first.Items.Single().Category.Should().Be("B");
        first.NextCursor.Should().Be(new GroupReportCursor(30, 2, 10));
        var next = (CursorPagedResult<CursorGroupRow, GroupReportCursor>)await Invoke(operations, "ReadAsync", request,
            new CursorPageRequest<GroupReportCursor>(first.NextCursor!, 1), CancellationToken.None);
        next.Items.Single().Category.Should().Be("A");
        next.HasMore.Should().BeFalse();
        fixture.Sql.Commands.Should().HaveCount(2).And.OnlyContain(command => command.Text.Contains("GROUP BY") && command.Text.Contains("ORDER BY") && command.Text.Contains("LIMIT"));
        fixture.Sql.Commands[1].Text.Should().Contain("HAVING");
    }

    [Fact]
    public async Task RegisteredAndCommandSideCursorsUseTheSelectedWorkAndTimeout()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases().UseCommandTimeout(3));
        var entity = builder.Entity<PipelineRow>();
        entity.HasQuery("Registered").FromProvider<TerminalQueryRoot, TerminalRequest, ConditionalSummary>(request => request)
            .OrderBy(row => row.Id).CursorPaged<int, TerminalRequest>((row, cursor, request) => row.Id > cursor + request.Adjustment, row => row.Id)
            .ValidateCursor<int, TerminalRequest>((cursor, request) => cursor >= request.Minimum, "Outside the allowed range.");
        Configure(entity.HasQuery("ForCommand").OnWriteRepository(), results: true);
        var root = new TerminalQueryRoot();
        await using var work = new RepositoryUnitOfWork<PipelineContext>(new ScopedDbContextLeaseProvider<PipelineContext>(fixture.Context));
        var assembly = ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true);
        var operations = Activator.CreateInstance(TypeOf(assembly, "PipelineRowRepositoryOperations"), work, root)!;
        await using var transaction = await work.BeginTransactionAsync();
        var registered = (CursorPagedResult<ConditionalSummary, int>)await Invoke(operations, "RegisteredAsync", new TerminalRequest(1, "", 0), new CursorPageRequest<int>(2, 2), CancellationToken.None);
        registered.Items.Select(row => row.Id).Should().Equal(4, 5);
        (await Page(operations, "ForCommandAsync", new CursorReportRequest(false, "A"), 2)).Items.Should().Equal(1, 3);
        root.SawTransaction.Should().BeTrue();
        fixture.Sql.Commands.Should().HaveCount(2).And.OnlyContain(command => command.Timeout == 3);
        await transaction.RollbackAsync();
        fixture.Context.Database.GetCommandTimeout().Should().Be(17);
        fixture.Context.DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task RequestAndCursorAdaptersExpandAcrossSeekMappingAndGuards()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        var id = builder.HasTypeAdapter<int, TerminalId>(value => new TerminalId(value), value => value.Value);
        builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id).OrderBy(value => value)
            .CursorPaged<TerminalId, TerminalAdaptedRequest>((value, cursor, request) => value > id.ToModel(cursor) + id.ToModel(request.Adjustment),
                (value, formatting) => id.ToContract(value))
            .ValidateCursor<TerminalId, TerminalAdaptedRequest>((cursor, request) => id.ToModel(cursor) >= 0, "Negative cursor.");
        var model = Model(fixture.Context, builder);
        model.Entities.Single().Queries.Single().Cursor!.SeekPredicateBodySource.Should().NotContain("ToModel");
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(model, rejectWarnings: true), "PipelineRowRepositoryOperations"), fixture.Context)!;
        var page = (CursorPagedResult<int, TerminalId>)await Invoke(operations, "ReadAsync", new TerminalAdaptedRequest(new TerminalId(1)),
            new CursorPageRequest<TerminalId>(new TerminalId(2), 2), CancellationToken.None);
        page.Items.Should().Equal(4, 5);
        page.NextCursor.Should().Be(new TerminalId(5));
        fixture.Sql.Commands.Should().ContainSingle();
    }

    [Theory]
    [InlineData("seek")]
    [InlineData("mapper")]
    [InlineData("guard")]
    public void CursorExpressionsRejectCapturedDesignerStateAndReservedInputs(string scenario)
    {
        using var context = MetadataContext();
        var builder = Builder();
        var entity = builder.Entity<PipelineRow>();
        var query = entity.HasQuery("Read").Select(row => row.Id).OrderBy(id => id);
        var captured = new CursorReportRequest(false, "A", 1);
        if (scenario == "seek")
        {
            query.CursorPaged<int, CursorReportRequest>((id, cursor, request) => id > cursor + captured.Offset, id => id);
        }

        if (scenario == "mapper")
        {
            query.CursorPaged<int, CursorReportRequest>((id, cursor) => id > cursor, (id, request) => id + captured.Offset);
        }

        if (scenario == "guard")
        {
            query.CursorPaged<int>((id, cursor) => id > cursor, id => id).ValidateCursor<int, CursorReportRequest>((cursor, request) => cursor > captured.Offset, "Outside scope.");
        }

        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*captures configuration-time state*");
        entity.ExcludeQuery("Read");
        var bad = entity.HasQuery("Bad").Select(row => row.Id).OrderBy(id => id);
        if (scenario == "seek")
        {
            bad.CursorPaged<int, CursorReportRequest>((id, cursor, entity) => id > cursor, id => id);
        }

        if (scenario == "mapper")
        {
            bad.CursorPaged<int, CursorReportRequest>((id, cursor) => id > cursor, (id, entity) => id);
        }

        if (scenario == "guard")
        {
            bad.CursorPaged<int>((id, cursor) => id > cursor, id => id).ValidateCursor<int, CursorReportRequest>((cursor, entity) => true, "Outside scope.");
        }

        generate.Should().Throw<InvalidOperationException>().WithMessage("*conflicts with generated infrastructure*");
    }

    [Fact]
    public async Task NullableCursorOnlyRequestsAreAnnotatedBeforeSignatureCapture()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id).OrderBy(id => id)
            .CursorPaged<ReportCursor, CursorReportRequest>((id, cursor, request) => id > cursor.Id + (request == null ? 0 : request.Offset),
                (id, formatting) => new ReportCursor(id, false, formatting == null ? null : formatting.Scope))
            .AllowNullInput<CursorReportRequest>("request");
        var model = Model(fixture.Context, builder);
        model.Entities.Single().Queries.Single().Parameters.Single().TypeName.Should().EndWith("?");
        var operations = Activator.CreateInstance(TypeOf(ClosedRepositoryTests.Compile(model, rejectWarnings: true), "PipelineRowRepositoryOperations"), fixture.Context)!;
        var result = (CursorPagedResult<int, ReportCursor>)await Invoke(operations, "ReadAsync", null!, new CursorPageRequest<ReportCursor>(new ReportCursor(2, false, null), 2), CancellationToken.None);
        result.Items.Should().Equal(3, 4);
        result.NextCursor.Should().Be(new ReportCursor(4, false, null));
    }

    [Fact]
    public void PairedCursorRebindingProtectsNestedLambdaScopes()
    {
        var builder = Builder();
        var query = builder.Entity<PipelineRow>().HasQuery("Read").Select(row => row.Id);
        Expression<Func<int, int, CursorReportRequest, bool>> seek = (id, cursor, request) => id > cursor;
        var row = Expression.Parameter(typeof(int), "id");
        var request = Expression.Parameter(typeof(CursorReportRequest), "formatting");
        var nestedRequest = seek.Parameters[2];
        var nested = Expression.Lambda<Func<CursorReportRequest, bool>>(Expression.Equal(Expression.Property(nestedRequest, nameof(CursorReportRequest.Offset)), Expression.Constant(7)), nestedRequest);
        var any = Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), [typeof(CursorReportRequest)],
            Expression.NewArrayInit(typeof(CursorReportRequest), Expression.New(typeof(CursorReportRequest).GetConstructors().Single(), Expression.Constant(false), Expression.Constant(null, typeof(string)), Expression.Constant(7))), nested);
        var mapper = Expression.Lambda<Func<int, CursorReportRequest, int>>(Expression.Condition(any, Expression.Property(request, nameof(CursorReportRequest.Offset)), Expression.Constant(-1)), row, request);
        query.CursorPaged<int, CursorReportRequest>(seek, mapper);
        var rebound = builder.Build().Entities.Single().Queries.Single().Cursor!.CursorSelector;
        var nestedRebound = (LambdaExpression)((MethodCallExpression)((ConditionalExpression)rebound.Body).Test).Arguments[1];
        nestedRebound.Parameters[0].Should().NotBeSameAs(rebound.Parameters[1]);
        rebound.Compile().DynamicInvoke(1, new CursorReportRequest(false, null, 42)).Should().Be(42);
    }

    private static void Configure(RepositoryQueryBuilder<PipelineRow> root, bool results)
    {
        root.Where<CursorReportRequest>((row, request) => request.Scope == null || row.Category == request.Scope).ProjectTo(row => row.Id);
        if (results)
        {
            root.Results<int>().OrderBy(id => id).OrderByDescendingIf((CursorReportRequest request) => request.Descending, id => id)
                .CursorPaged<ReportCursor, CursorReportRequest>((id, cursor, request) => request.Descending ? id < cursor.Id : id > cursor.Id,
                    (id, formatting) => new ReportCursor(id, formatting.Descending, formatting.Scope))
                .ValidateCursor<ReportCursor, CursorReportRequest>((cursor, request) => cursor.Descending == request.Descending && cursor.Scope == request.Scope, "The cursor belongs to a different direction or scope.");
        }
        else
        {
            root.OrderBy<int, CursorReportRequest>((row, request) => request.Descending ? -row.Id : row.Id)
                .CursorPaged<ReportCursor, CursorReportRequest>((row, cursor, request) => request.Descending ? row.Id < cursor.Id : row.Id > cursor.Id,
                    (row, formatting) => new ReportCursor(row.Id, formatting.Descending, formatting.Scope))
                .ValidateCursor<ReportCursor, CursorReportRequest>((cursor, request) => cursor.Descending == request.Descending && cursor.Scope == request.Scope, "The cursor belongs to a different direction or scope.");
        }
    }

    private static RepositoryModelBuilder Builder(string surface = "closed")
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        if (surface == "closed")
        {
            builder.Configure(options => options.UseClosedRepositories("CursorRequest.Contracts"));
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

    private static object Create(Assembly assembly, PipelineContext context, string surface)
    {
        var repository = Activator.CreateInstance(TypeOf(assembly, surface == "closed" ? "PipelineRowRepositoryOperations" : "PipelineRowRepository"), context)!;
        return surface == "cqrs" ? Activator.CreateInstance(TypeOf(assembly, "PipelineRowReadRepository"), repository)! : repository;
    }

    private static MethodInfo Method(object target, string name) => target.GetType().GetMethod(name) ?? target.GetType().Assembly.GetTypes()
        .Where(type => type.Name == "PipelineRowConfiguredQueryExtensions").SelectMany(type => type.GetMethods()).Single(method => method.Name == name);

    private static async Task<object> Invoke(object target, string name, params object[] arguments)
    {
        Task task;
        try
        {
            var method = Method(target, name);
            task = (Task)method.Invoke(method.IsStatic ? null : target, method.IsStatic ? new object[] { target }.Concat(arguments).ToArray() : arguments)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static async Task<(IReadOnlyList<int> Items, bool HasMore, ReportCursor? Cursor)> Page(object target, string name, CursorReportRequest request,
        int size, ReportCursor? cursor = null, CancellationToken cancellation = default)
    {
        var pageType = Method(target, name).GetParameters().Single(parameter => parameter.Name == "cursorPage").ParameterType;
        var factory = pageType.GetMethod(cursor is null ? "First" : "After", BindingFlags.Public | BindingFlags.Static);
        object[] arguments = cursor is null ? [size] : [cursor, size];
        var page = factory is null ? Activator.CreateInstance(pageType, arguments)! : factory.Invoke(null, arguments)!;
        var result = await Invoke(target, name, request, page, cancellation);
        var type = result.GetType();
        return ((IReadOnlyList<int>)type.GetProperty("Items")!.GetValue(result)!, (bool)type.GetProperty("HasMore")!.GetValue(result)!, (ReportCursor?)type.GetProperty("NextCursor")!.GetValue(result));
    }

    private static void CompileFiles(IEnumerable<string> sources)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(RequestCursorTests).Assembly.Location).Append(typeof(DbContext).Assembly.Location)
            .Append(typeof(RelationalQueryableExtensions).Assembly.Location).Append(typeof(IDbContextLeaseProvider<>).Assembly.Location)
            .Append(typeof(PagedResult<>).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create("RequestCursors_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)),
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
        public List<(string Text, int Timeout)> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add((command.CommandText, command.CommandTimeout));
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

    private sealed class CountingCache(IRepositoryResultCache inner) : IRepositoryResultCache
    {
        public int Reads { get; private set; }

        public Task<T> GetOrCreateAsync<T>(string key, IReadOnlyCollection<string> tags, RepositoryCacheOptions options,
            Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default)
        {
            Reads++;
            return inner.GetOrCreateAsync(key, tags, options, factory, cancellationToken);
        }

        public Task InvalidateAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default) => inner.InvalidateAsync(tags, cancellationToken);
    }
}

public sealed record CursorReportRequest(bool Descending, string? Scope, int Offset = 0);

public sealed record ReportCursor(int Id, bool Descending, string? Scope);

public sealed record GroupReportCursor(int Total, int FirstId, int Offset);

public sealed class CursorGroupRow
{
    public string Category { get; init; } = "";
    public int Total { get; init; }
    public int FirstId { get; init; }
}

public sealed class CursorGuardRequest(CancellationTokenSource cancellation)
{
    public int Reads { get; private set; }

    public bool IsValid
    {
        get
        {
            Reads++;
            cancellation.Cancel();
            return true;
        }
    }
}