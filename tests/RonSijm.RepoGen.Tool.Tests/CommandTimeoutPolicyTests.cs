using System.Data.Common;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.Sqlite;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CommandTimeoutPolicyTests
{
    [Theory]
    [InlineData("entity", 0)]
    [InlineData("entity", -1)]
    [InlineData("query", 0)]
    [InlineData("query", -1)]
    [InlineData("result", 0)]
    [InlineData("result", -1)]
    [InlineData("command", 0)]
    [InlineData("command", -1)]
    public void InvalidOverridesAreRejectedAtTheDesignerBoundary(string target, int seconds)
    {
        var entity = new RepositoryModelBuilder().Entity<CommandRow>();
        Action configure = target switch
        {
            "entity" => () => entity.UseCommandTimeout(seconds),
            "query" => () => entity.HasQuery("Read").UseCommandTimeout(seconds),
            "result" => () => entity.HasQuery("Read").Select(row => row.Name).UseCommandTimeout(seconds),
            _ => () => entity.HasCommand("Write").UseCommandTimeout(seconds)
        };
        configure.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*RGD2301*");
    }

    [Theory]
    [InlineData("entity", false)]
    [InlineData("entity", true)]
    [InlineData("query", false)]
    [InlineData("query", true)]
    [InlineData("command", false)]
    [InlineData("command", true)]
    public void NonClosedSurfacesCannotSilentlyIgnoreAnOverride(string target, bool cqrs)
    {
        using var context = MetadataContext();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseCqrsSelectors(cqrs).GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<CommandRow>();
        if (target == "entity")
        {
            entity.UseCommandTimeout(null);
        }
        else if (target == "query")
        {
            entity.HasQuery("Read").UseCommandTimeout(3).Count();
        }
        else
        {
            entity.HasCommand("Write").UseCommandTimeout(3).Delete();
        }

        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*RGD2302*closed*restore*");
    }

    [Fact]
    public void DisabledOperationsAndExcludedEntitiesDoNotActivateTimeoutPolicies()
    {
        using var context = MetadataContext();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseCqrsSelectors().GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<CommandRow>();
        entity.ReadEnabled = false;
        entity.WriteEnabled = false;
        entity.HasQuery("Disabled").UseCommandTimeout(3).Count();
        entity.HasCommand("Disabled").UseCommandTimeout(null).Delete();
        var model = Model(context, builder);
        model.Entities.Single().Queries.Should().BeEmpty();
        model.Entities.Single().Commands.Should().BeEmpty();
        entity.UseCommandTimeout(3);
        entity.RepositoryEnabled = false;
        builder.Entity<CommandOwner>();
        Model(context, builder).Entities.Should().ContainSingle().Which.EntityName.Should().Be(nameof(CommandOwner));
    }

    [Fact]
    public void ExplicitContextSettingAndInheritanceAreDifferentAndResetsAreLocal()
    {
        using var context = MetadataContext();
        var builder = Builder();
        var entity = builder.Entity<CommandRow>().UseCommandTimeout(5);
        var query = entity.HasQuery("Read").UseCommandTimeout(null).Count();
        var command = entity.HasCommand("Write").UseCommandTimeout(11).Delete();
        var result = entity.HasQuery("Result").Select(row => row.Name).UseCommandTimeout(2).List();
        var model = Model(context, builder).Entities.Single();
        model.CommandTimeout.Should().Be(new ConfiguredCommandTimeoutModel(5, "entity"));
        model.Queries.Single(row => row.MethodName == "ReadAsync").CommandTimeout.Should().Be(new ConfiguredCommandTimeoutModel(null, "operation"));
        model.Commands.Single().CommandTimeout.Should().Be(new ConfiguredCommandTimeoutModel(11, "operation"));
        query.InheritCommandTimeout();
        command.InheritCommandTimeout();
        result.InheritCommandTimeout();
        var inherited = Model(context, builder).Entities.Single();
        inherited.Queries.Should().OnlyContain(row => row.CommandTimeout == new ConfiguredCommandTimeoutModel(5, "entity"));
        inherited.Commands.Single().CommandTimeout.Should().Be(new ConfiguredCommandTimeoutModel(5, "entity"));
        entity.UseCommandTimeout(null);
        Model(context, builder).Entities.Single().Queries.Should().OnlyContain(row => row.CommandTimeout == new ConfiguredCommandTimeoutModel(null, "entity"));
        entity.InheritCommandTimeout();
        var reset = Model(context, builder);
        reset.Entities.Single().CommandTimeout.Should().BeNull();
        reset.Entities.Single().Queries.Should().OnlyContain(row => row.CommandTimeout == null);
        reset.Settings.CommandTimeoutSeconds.Should().Be(7);
        // Previously captured models are independent of subsequent designer edits.
        model.CommandTimeout!.Seconds.Should().Be(5);
        model.Queries.Single(row => row.MethodName == "ResultAsync").CommandTimeout!.Seconds.Should().Be(2);
    }

    [Fact]
    public void StaleResultBuildersCannotChangeTheActiveQueryPolicy()
    {
        var builder = Builder();
        var entity = builder.Entity<CommandRow>();
        var original = entity.HasQuery("Read").Select(row => row.Name).UseCommandTimeout(2);
        original.Select(name => name.Length).UseCommandTimeout(3).List();
        Action change = () => original.UseCommandTimeout(4);
        Action reset = () => original.InheritCommandTimeout();
        change.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        reset.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        builder.Build().Entities.Single().Queries.Single().CommandTimeout!.Seconds.Should().Be(3);
    }

    [Fact]
    public void ResettingEveryOverridePreservesTheOriginalGeneratedFilesByteForByte()
    {
        using var context = MetadataContext();
        var plain = Builder();
        plain.Entity<CommandRow>().HasQuery("Read").Select(row => row.Name).List();
        plain.Entity<CommandRow>().HasCommand("Delete").Delete();
        var reset = Builder();
        var entity = reset.Entity<CommandRow>().UseCommandTimeout(5).InheritCommandTimeout();
        entity.HasQuery("Read").UseCommandTimeout(null).InheritCommandTimeout().Select(row => row.Name).UseCommandTimeout(3).InheritCommandTimeout().List();
        entity.HasCommand("Delete").UseCommandTimeout(11).InheritCommandTimeout().Delete();
        RepositoryGenerator.Generate(Model(context, plain)).Files.Should().Equal(RepositoryGenerator.Generate(Model(context, reset)).Files);
    }

    [Fact]
    public void ManifestRecordsTheEffectiveOverridesWithoutChangingServiceContractsOrApiSnapshots()
    {
        using var context = MetadataContext();
        var builder = Builder();
        var entity = builder.Entity<CommandRow>();
        var first = entity.HasQuery("Find").Where<int>((row, id) => row.Id == id).Select(row => row.Name).List();
        var second = entity.HasQuery("Find").Where<string>((row, name) => row.Name == name).Select(row => row.Name).List();
        var command = entity.HasCommand("Delete").Delete();
        var before = Model(context, builder);
        entity.UseCommandTimeout(5);
        first.UseCommandTimeout(2);
        second.UseCommandTimeout(null);
        command.UseCommandTimeout(11);
        var after = Model(context, builder);
        ClosedRepositorySourceRenderer.RenderContracts(after).Should().Be(ClosedRepositorySourceRenderer.RenderContracts(before));
        RepositoryApiSnapshot.Capture(after).ToJson().Should().Be(RepositoryApiSnapshot.Capture(before).ToJson());
        var json = JsonNode.Parse(RepositoryManifestRenderer.Render(after))!;
        json["commandTimeoutSeconds"]!.GetValue<int>().Should().Be(7);
        var inventory = json["entities"]![0]!;
        inventory["commandTimeout"]!["source"]!.GetValue<string>().Should().Be("entity");
        inventory["queries"]![0]!["commandTimeout"]!["seconds"]!.GetValue<int>().Should().Be(2);
        inventory["queries"]![1]!["commandTimeout"]!["seconds"].Should().BeNull();
        inventory["queries"]![1]!["commandTimeout"]!["source"]!.GetValue<string>().Should().Be("operation");
        inventory["commands"]![0]!["commandTimeout"]!["seconds"]!.GetValue<int>().Should().Be(11);
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(after);
        var compilation = CSharpCompilation.Create("TimeoutContracts", [CSharpSyntaxTree.ParseText(contracts)],
            ClosedRepositoryTests.PlatformReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        contracts.Should().NotContain("CommandTimeout").And.NotContain("DbContext").And.NotContain("EntityFrameworkCore");
    }

    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void EveryClosedLayoutCompilesWithoutWarnings(RepositoryOutputLayout layout)
    {
        using var context = MetadataContext();
        var builder = QueryBuilder();
        var files = RepositoryGenerator.Generate(Model(context, builder), layout).Files;
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(CommandTimeoutPolicyTests).Assembly.Location).Append(typeof(DbContext).Assembly.Location)
            .Append(typeof(RelationalQueryableExtensions).Assembly.Location).Append(typeof(IDbContextLeaseProvider<>).Assembly.Location)
            .Append(typeof(PagedResult<>).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create("Timeout_" + Guid.NewGuid().ToString("N"),
            files.Where(file => file.Kind != RepositoryOutputKind.Manifest).Select(file => CSharpSyntaxTree.ParseText(file.Contents)),
            paths.Select(path => MetadataReference.CreateFromFile(path)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error || item.Severity == DiagnosticSeverity.Warning && item.Id != "CS1701").Should().BeEmpty();
        output.Position = 0;
        AssemblyLoadContext.Default.LoadFromStream(output).Should().NotBeNull();
    }

    [Fact]
    public async Task QueriesHonorOperationEntityAndModelTimeoutsAndRestoreTheBorrowedContext()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = QueryBuilder();
        builder.Entity<CommandOwner>().HasQuery("Owners").Count();
        var assembly = ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true);
        var operations = fixture.Operations(assembly);
        (await Call<Task<int>>(operations, "EntityCountAsync", CancellationToken.None)).Should().Be(3);
        fixture.ExpectSql(5, 1);
        (await Call<Task<IReadOnlyList<string>>>(operations, "NamesAsync", CancellationToken.None)).Should().HaveCount(3);
        fixture.ExpectSql(3, 1);
        (await Call<Task<int>>(operations, "ContextCountAsync", CancellationToken.None)).Should().Be(3);
        fixture.ExpectSql(17, 1);
        (await Call<Task<int>>(operations, "WriteCountAsync", CancellationToken.None)).Should().Be(3);
        fixture.ExpectSql(4, 1);
        var owners = Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandOwnerRepositoryOperations")!, fixture.Context)!;
        (await Call<Task<int>>(owners, "OwnersAsync", CancellationToken.None)).Should().Be(1);
        fixture.ExpectSql(7, 1);
        fixture.Context.DisposeCount.Should().Be(0);
    }

    [Theory]
    [InlineData("Page", 2)]
    [InlineData("GroupedPage", 2)]
    [InlineData("FromPage", 2)]
    [InlineData("Slice", 1)]
    public async Task ResultPipelinesApplyOnePolicyToAllSqlCommands(string operation, int sqlCount)
    {
        await using var fixture = await Fixture.CreateAsync();
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, QueryBuilder()), rejectWarnings: true));
        if (operation == "Slice")
        {
            (await Call<Task<SliceResult<string>>>(operations, "SliceAsync", 1, 2, CancellationToken.None)).Items.Should().HaveCount(2);
        }
        else
        {
            (await Call<Task<PagedResult<string>>>(operations, operation + "Async", 1, 2, CancellationToken.None)).TotalCount.Should().BeGreaterThan(0);
        }

        fixture.ExpectSql(2, sqlCount);
        fixture.Context.Database.CurrentTransaction.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(17)]
    public async Task StreamingKeepsTheOverrideUntilEarlyDisposalAndRestoresNullableOriginalSettings(int? original)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Database.SetCommandTimeout(original);
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, QueryBuilder())));
        var stream = Call<IAsyncEnumerable<string>>(operations, "StreamAsync", CancellationToken.None);
        fixture.Context.Database.GetCommandTimeout().Should().Be(original);
        var enumerator = stream.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        fixture.Context.Database.GetCommandTimeout().Should().Be(6);
        fixture.Sql.Commands.Should().ContainSingle().Which.Timeout.Should().Be(6);
        await enumerator.DisposeAsync();
        fixture.Context.Database.GetCommandTimeout().Should().Be(original);
        fixture.Context.DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task StreamCancellationRestoresTheOriginalSetting()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var canceled = new CancellationTokenSource();
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, QueryBuilder())));
        var enumerator = Call<IAsyncEnumerable<string>>(operations, "StreamAsync", canceled.Token).GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        canceled.Cancel();
        Func<Task> advance = async () => await enumerator.MoveNextAsync();
        await advance.Should().ThrowAsync<OperationCanceledException>();
        await enumerator.DisposeAsync();
        fixture.Context.Database.GetCommandTimeout().Should().Be(17);
    }

    [Theory]
    [InlineData("dictionary")]
    [InlineData("translation")]
    [InlineData("sqlCancellation")]
    public async Task QueryFailuresRestoreTimeouts(string failure)
    {
        await using var fixture = await Fixture.CreateAsync();
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, QueryBuilder())));
        if (failure == "sqlCancellation")
        {
            fixture.Sql.Cancel = true;
        }

        Func<Task> execute = failure switch
        {
            "dictionary" => async () => await Call<Task<IReadOnlyDictionary<int, string>>>(operations, "DuplicatesAsync", CancellationToken.None),
            "translation" => async () => await Call<Task<int>>(operations, "UntranslatableAsync", CancellationToken.None),
            _ => async () => await Call<Task<IReadOnlyList<string>>>(operations, "NamesAsync", CancellationToken.None)
        };
        if (failure == "dictionary")
        {
            await execute.Should().ThrowAsync<ArgumentException>();
        }
        else if (failure == "translation")
        {
            await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("*translated*");
        }
        else
        {
            await execute.Should().ThrowAsync<OperationCanceledException>();
        }

        fixture.Context.Database.GetCommandTimeout().Should().Be(17);
        if (failure == "translation")
        {
            fixture.Sql.Commands.Should().BeEmpty();
        }
        else
        {
            fixture.Sql.Commands.Should().ContainSingle().Which.Timeout.Should().Be(3);
        }
    }

    [Fact]
    public async Task OwnedAndKeyedLeasesUseTheSelectedContextAndRestoreBeforeDisposal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owned = new TimeoutLeaseProvider(fixture.Options);
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases());
        builder.Entity<CommandRow>().HasQuery("Read").UseCommandTimeout(3).Count();
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, builder)), owned);
        (await Call<Task<int>>(operations, "ReadAsync", CancellationToken.None)).Should().Be(3);
        owned.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
        owned.Contexts[0].TimeoutAtDisposal.Should().Be(17);
        fixture.Sql.Commands.Should().ContainSingle().Which.Timeout.Should().Be(3);
        fixture.Sql.Commands.Clear();
        var keyed = new TimeoutKeyedProvider(fixture.Context);
        var keys = Builder();
        keys.Configure(options => options.UseKeyedContextLeases<string>());
        keys.Entity<CommandRow>().UseCommandTimeout(5).HasQuery("Read").UseCommandTimeout(2)
            .UseContextKey<TimeoutRequest, string>(request => request.Database).Where<TimeoutRequest>((row, request) => row.Id >= request.Minimum).Count();
        var selected = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, keys)), keyed);
        (await Call<Task<int>>(selected, "ReadAsync", new TimeoutRequest("reports", 2), CancellationToken.None)).Should().Be(2);
        keyed.Keys.Should().Equal("reports");
        fixture.ExpectSql(2, 1);
        fixture.Context.DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task InputGuardsAndPreCanceledCallsDoNotAcquireOrChangeTheContext()
    {
        await using var fixture = await Fixture.CreateAsync();
        var leases = new TimeoutLeaseProvider(fixture.Options);
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases());
        builder.Entity<CommandRow>().HasQuery("Read").UseCommandTimeout(2)
            .Validate<TimeoutRequest>(request => request.Minimum > 0, "Positive minimum required.")
            .Where<TimeoutRequest>((row, request) => row.Id >= request.Minimum).Count();
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, builder)), leases);
        Func<Task> invalid = async () => await Call<Task<int>>(operations, "ReadAsync", new TimeoutRequest("db", 0), CancellationToken.None);
        await invalid.Should().ThrowAsync<ArgumentException>();
        Func<Task> canceled = async () => await Call<Task<int>>(operations, "ReadAsync", new TimeoutRequest("db", 1), new CancellationToken(true));
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        leases.Contexts.Should().BeEmpty();
        fixture.Sql.Commands.Should().BeEmpty();
        fixture.Context.Database.GetCommandTimeout().Should().Be(17);
    }

    [Fact]
    public async Task RegisteredRootsSeeTheOverrideOnTheSharedTransactionalContext()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseAsyncContextLeases());
        builder.Entity<CommandRow>().UseCommandTimeout(5).HasQuery("Read").UseCommandTimeout(3)
            .FromProvider<TimeoutQueryRoot, SqlNameRequest, SqlNameRow>(request => request).OrderBy(row => row.Id).Paged();
        var root = new TimeoutQueryRoot();
        await using var work = new RepositoryUnitOfWork<CommandContext>(fixture.Context);
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, builder)), work, root);
        await using var transaction = await work.BeginTransactionAsync();
        var page = await Call<Task<PagedResult<SqlNameRow>>>(operations, "ReadAsync", new SqlNameRequest("Ada"), 1, 2, CancellationToken.None);
        page.TotalCount.Should().Be(1);
        root.Context.Should().BeSameAs(fixture.Context);
        root.Timeout.Should().Be(3);
        root.SawTransaction.Should().BeTrue();
        fixture.ExpectSql(3, 2);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TrackedAndSetBasedWritesRestoreTimeoutsAndStagedWritesDoNotLeakPolicyIntoLaterSaves()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = WriteBuilder();
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true));
        (await Call<Task<int>>(operations, "UpdateAsync", CancellationToken.None)).Should().Be(3);
        fixture.ExpectSql(11, 1);
        (await Call<Task<CommandResult<int>>>(operations, "CreateAsync", new CreateCommand("New", null, 1), CancellationToken.None)).Status.Should().Be(CommandStatus.Applied);
        fixture.ExpectSql(12, 1);
        (await Call<Task<CommandResult<int>>>(operations, "PatchAsync", new OptionalPatch(new Abstractions.Optional<string?>("patched")), CancellationToken.None)).Status.Should().Be(CommandStatus.Applied);
        fixture.Sql.Commands.Should().HaveCount(3);
        fixture.ExpectSql(13, 3);
        (await Call<Task<int>>(operations, "DeleteAsync", "New", CancellationToken.None)).Should().Be(1);
        fixture.ExpectSql(5, 1);
        await using var work = new RepositoryUnitOfWork<CommandContext>(fixture.Context);
        var staged = Builder();
        staged.Configure(options => options.UseAsyncContextLeases());
        staged.Entity<CommandRow>().HasCommand("StageDelete").UseCommandTimeout(9).Where(row => row.Id == 2).TrackedDelete().Stage();
        var stageOperations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, staged)), work);
        (await Call<Task<CommandResult>>(stageOperations, "StageDeleteAsync", CancellationToken.None)).Status.Should().Be(CommandStatus.Staged);
        fixture.ExpectSql(9, 1);
        (await work.SaveChangesAsync()).Should().Be(1);
        fixture.ExpectSql(17, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedWriteFailuresRestoreTimeoutsWithoutMisrepresentingCommitState(bool mappingFailure)
    {
        await using var fixture = await Fixture.CreateAsync();
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, WriteBuilder())));
        Func<Task> failure = mappingFailure
            ? async () => await Call<Task<CommandResult<CommandDto>>>(operations, "FailingResultAsync", new CreateCommand("Saved", null, 1), CancellationToken.None)
            : async () => await Call<Task<CommandResult<int>>>(operations, "CreateAsync", new CreateCommand("Ada", null, 1), CancellationToken.None);
        if (mappingFailure)
        {
            await failure.Should().ThrowAsync<RepositorySavedResultException>();
        }
        else
        {
            await failure.Should().ThrowAsync<DbUpdateException>();
        }

        fixture.ExpectSql(mappingFailure ? 14 : 12, 1);
        (await fixture.Context.Rows.AsNoTracking().AnyAsync(row => row.Name == "Saved")).Should().Be(mappingFailure);
    }

    [Theory]
    [InlineData("atomic", false)]
    [InlineData("atomic", true)]
    [InlineData("returning", false)]
    [InlineData("returning", true)]
    public async Task NativeWritersUseTheTimeoutAndMappingFailureRollsBack(string kind, bool failure)
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        var entity = builder.Entity<CommandRow>().UseCommandTimeout(5);
        object writer;
        if (kind == "atomic")
        {
            builder.Configure(options => options.UseAtomicWrites<TimeoutAtomicWriter>());
            writer = new TimeoutAtomicWriter();
            var command = entity.HasCommand("Execute").UseCommandTimeout(8).Create<CreateCommand>(request => new CommandRow { Name = request.Name, OwnerId = request.OwnerId, Revision = 1 })
                .AllowWrite(row => row.OwnerId).AllowWrite(row => row.Revision).InsertIfAbsent(row => row.Name);
            if (failure)
            {
                command.Returns(row => CommandMapping.Fail(row));
            }
            else
            {
                command.Returns(row => new CommandDto(row.Id, row.Name, row.Description, row.Revision, row.Normalized));
            }
        }
        else
        {
            builder.Configure(options => options.UseReturningWrites<TimeoutReturningWriter>());
            writer = new TimeoutReturningWriter();
            var command = entity.HasCommand("Execute").UseCommandTimeout(8).Where(row => row.Id == 1)
                .Update(setters => setters.SetProperty(row => row.Description, "native"));
            if (failure)
            {
                command.ReturnsMany(row => TimeoutPredicates.FailDto(row.Id));
            }
            else
            {
                command.ReturnsMany(row => new CommandDto(row.Id, row.Name, row.Description, row.Revision, row.Normalized));
            }
        }

        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, builder), rejectWarnings: true), fixture.Context, writer);
        if (kind == "atomic")
        {
            Func<Task> execute = async () => await Call<Task<CommandResult<CommandDto>>>(operations, "ExecuteAsync", new CreateCommand("Native", null, 1), CancellationToken.None);
            if (failure)
            {
                await execute.Should().ThrowAsync<InvalidOperationException>();
            }
            else
            {
                await execute();
            }

            ((TimeoutAtomicWriter)writer).Timeout.Should().Be(8);
            fixture.Context.Database.GetCommandTimeout().Should().Be(17);
            (await fixture.Context.Rows.AsNoTracking().AnyAsync(row => row.Name == "Native")).Should().Be(!failure);
        }
        else
        {
            Func<Task> execute = async () => await Call<Task<MutationResult<CommandDto>>>(operations, "ExecuteAsync", CancellationToken.None);
            if (failure)
            {
                await execute.Should().ThrowAsync<InvalidOperationException>();
            }
            else
            {
                await execute();
            }

            ((TimeoutReturningWriter)writer).Timeout.Should().Be(8);
            fixture.Context.Database.GetCommandTimeout().Should().Be(17);
            (await fixture.Context.Rows.AsNoTracking().SingleAsync(row => row.Id == 1)).Description.Should().Be(failure ? null : "native");
        }

        fixture.Context.Database.CurrentTransaction.Should().BeNull();
    }

    [Fact]
    public async Task CacheHitsFillFailuresAndPostCommitInvalidationFailuresRestoreTimeouts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Configure(options => options.UseClosedCaching("timeout-tests"));
        var entity = builder.Entity<CommandRow>().UseCommandTimeout(5);
        entity.HasQuery("Read").UseCommandTimeout(3).Cache().Select(row => row.Name).List();
        entity.HasCommand("Write").UseCommandTimeout(11).Update(setters => setters.SetProperty(row => row.Description, "cached-write"));
        var cache = new TimeoutCache(fixture.Context);
        var operations = fixture.Operations(ClosedRepositoryTests.Compile(Model(fixture.Context, builder)), fixture.Context, cache);
        (await Call<Task<IReadOnlyList<string>>>(operations, "ReadAsync", CancellationToken.None)).Should().HaveCount(3);
        fixture.ExpectSql(3, 1);
        (await Call<Task<IReadOnlyList<string>>>(operations, "ReadAsync", CancellationToken.None)).Should().HaveCount(3);
        fixture.ExpectSql(3, 0);
        cache.FailFill = true;
        Func<Task> fill = async () => await Call<Task<IReadOnlyList<string>>>(operations, "ReadAsync", CancellationToken.None);
        await fill.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cache fill*");
        fixture.Context.Database.GetCommandTimeout().Should().Be(17);
        cache.FillTimeouts.Should().Equal(3, 3, 3);
        cache.FailInvalidation = true;
        Func<Task> write = async () => await Call<Task<int>>(operations, "WriteAsync", CancellationToken.None);
        await write.Should().ThrowAsync<RepositoryCommitObserverException>();
        fixture.ExpectSql(11, 1);
        cache.InvalidationTimeout.Should().Be(11);
        (await fixture.Context.Rows.AsNoTracking().CountAsync(row => row.Description == "cached-write")).Should().Be(3);
    }

    private static RepositoryModelBuilder Builder()
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Timeout.Contracts").UseCommandTimeout(7).GenerateOnlyConfiguredEntities());
        return builder;
    }

    private static RepositoryModelBuilder QueryBuilder()
    {
        var builder = Builder();
        var entity = builder.Entity<CommandRow>().UseCommandTimeout(5);
        entity.HasQuery("EntityCount").Count();
        entity.HasQuery("Names").UseCommandTimeout(3).OrderBy(row => row.Id).Select(row => row.Name).List();
        entity.HasQuery("ContextCount").UseCommandTimeout(null).Count();
        entity.HasQuery("WriteCount").OnWriteRepository().UseCommandTimeout(4).Count();
        entity.HasQuery("Stream").UseCommandTimeout(6).OrderBy(row => row.Id).Select(row => row.Name).Stream();
        entity.HasQuery("Duplicates").UseCommandTimeout(3).Results<CommandRow>().Dictionary(row => row.OwnerId, row => row.Name);
        entity.HasQuery("Untranslatable").UseCommandTimeout(3).Where(row => TimeoutPredicates.Matches(row.Name)).Count();
        entity.HasQuery("Page").Select(row => row.Name).UseCommandTimeout(2).OrderBy(name => name).Paged(RepositoryPageConsistency.Serializable);
        entity.HasQuery("GroupedPage").Results<CommandRow>().GroupBy(row => row.OwnerId, group => new { Owner = group.Key, Count = group.Count() })
            .Where(group => group.Count > 0).OrderBy(group => group.Owner).Select(group => "Owner").UseCommandTimeout(2).Paged();
        entity.HasQuery("FromPage").From(sources => sources.Set<CommandRow>().OrderBy(row => row.Id).Select(row => row.Name)).UseCommandTimeout(2).Paged();
        entity.HasQuery("Slice").Select(row => row.Name).UseCommandTimeout(2).OrderBy(name => name).Slice();
        return builder;
    }

    private static RepositoryModelBuilder WriteBuilder()
    {
        var builder = Builder();
        var entity = builder.Entity<CommandRow>().UseCommandTimeout(5);
        entity.HasCommand("Update").UseCommandTimeout(11).Update(setters => setters.SetProperty(row => row.Description, "changed"));
        entity.HasCommand("Delete").Where<string>((row, name) => row.Name == name).Delete();
        entity.HasCommand("Create").UseCommandTimeout(12).Create<CreateCommand>(request => new CommandRow { Name = request.Name, Description = request.Description, OwnerId = request.OwnerId })
            .AllowWrite(row => row.OwnerId).Save().Returns(row => row.Id);
        entity.HasCommand("Patch").UseCommandTimeout(13).Where(row => row.Id == 1)
            .Patch(patch => patch.SetPropertyWhen<string?, OptionalPatch>(row => row.Description, (row, request) => request.Description.Value, request => request.Description.IsSpecified))
            .Save().ReloadAfterSave().Returns(row => row.Id);
        entity.HasCommand("FailingResult").UseCommandTimeout(14).Create<CreateCommand>(request => new CommandRow { Name = request.Name, OwnerId = request.OwnerId })
            .AllowWrite(row => row.OwnerId).Save().Returns(row => CommandMapping.Fail(row));
        return builder;
    }

    private static CommandContext MetadataContext() => new(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);

    private static RepositoryGenerationModel Model(CommandContext context, RepositoryModelBuilder builder) =>
        RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build());

    private static T Call<T>(object operations, string method, params object[] arguments) => (T)operations.GetType().GetMethod(method)!.Invoke(operations, arguments)!;

    private sealed class Fixture(SqliteConnection connection, CommandContext context, DbContextOptions<CommandContext> options, SqlRecorder sql) : IAsyncDisposable
    {
        public CommandContext Context { get; } = context;
        public DbContextOptions<CommandContext> Options { get; } = options;
        public SqlRecorder Sql { get; } = sql;

        public object Operations(Assembly assembly, object? source = null, params object[] extras) =>
            Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, new[] { source ?? Context }.Concat(extras).ToArray())!;

        public void ExpectSql(int timeout, int count)
        {
            Sql.Commands.Should().HaveCount(count);
            if (count > 0)
            {
                Sql.Commands.Should().OnlyContain(command => command.Timeout == timeout);
            }

            Context.Database.GetCommandTimeout().Should().Be(17);
            Sql.Commands.Clear();
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var sql = new SqlRecorder();
            var options = new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection, sqlite => sqlite.CommandTimeout(17)).AddInterceptors(sql).Options;
            var context = new CommandContext(options);
            await context.Database.EnsureCreatedAsync();
            context.Owners.Add(new CommandOwner { Id = 1 });
            context.Rows.AddRange(new CommandRow { Id = 1, Name = "Ada", OwnerId = 1 }, new CommandRow { Id = 2, Name = "Grace", OwnerId = 1 }, new CommandRow { Id = 3, Name = "Ron", OwnerId = 1 });
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

    private sealed class SqlRecorder : DbCommandInterceptor
    {
        public List<(int Timeout, string Sql)> Commands { get; } = [];
        public bool Cancel { get; set; }

        private void Record(DbCommand command)
        {
            Commands.Add((command.CommandTimeout, command.CommandText));
            if (Cancel)
            {
                throw new OperationCanceledException("Intercepted SQL cancellation.");
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TimeoutLeaseProvider(DbContextOptions<CommandContext> options) : IDbContextLeaseProvider<CommandContext>
    {
        public List<TimeoutOwnedContext> Contexts { get; } = [];

        public ValueTask<DbContextLease<CommandContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new TimeoutOwnedContext(options);
            Contexts.Add(context);
            return ValueTask.FromResult(DbContextLease.Owned<CommandContext>(context));
        }
    }

    private sealed class TimeoutKeyedProvider(CommandContext context) : IKeyedDbContextLeaseProvider<CommandContext, string>
    {
        public List<string> Keys { get; } = [];

        public ValueTask<DbContextLease<CommandContext>> AcquireAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Keys.Add(key);
            return ValueTask.FromResult(DbContextLease.Borrowed(context));
        }
    }

    private sealed class TimeoutCache(CommandContext context) : IRepositoryResultCache
    {
        private object? cached;
        public bool FailFill { get; set; }
        public bool FailInvalidation { get; set; }
        public List<int?> FillTimeouts { get; } = [];
        public int? InvalidationTimeout { get; private set; }

        public async Task<T> GetOrCreateAsync<T>(string key, IReadOnlyCollection<string> tags, RepositoryCacheOptions options, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default)
        {
            FillTimeouts.Add(context.Database.GetCommandTimeout());
            if (FailFill)
            {
                throw new InvalidOperationException("Intentional cache fill failure.");
            }

            if (cached is T value)
            {
                return value;
            }

            var result = await factory(cancellationToken);
            cached = result;
            return result;
        }

        public Task InvalidateAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
        {
            InvalidationTimeout = context.Database.GetCommandTimeout();
            if (FailInvalidation)
            {
                throw new InvalidOperationException("Intentional invalidation failure.");
            }

            cached = null;
            return Task.CompletedTask;
        }
    }
}

public sealed record TimeoutRequest(string Database, int Minimum);

public static class TimeoutPredicates
{
    public static bool Matches(string name) => name.Length > 0;
    public static CommandDto FailDto(int id) => throw new InvalidOperationException("Intentional returned projection failure.");
}

public sealed class TimeoutQueryRoot : IRepositoryQueryRoot<CommandContext, SqlNameRequest, SqlNameRow>
{
    public CommandContext? Context { get; private set; }
    public int? Timeout { get; private set; }
    public bool SawTransaction { get; private set; }

    public IQueryable<SqlNameRow> CreateQuery(CommandContext context, SqlNameRequest request)
    {
        Context = context;
        Timeout = context.Database.GetCommandTimeout();
        SawTransaction = context.Database.CurrentTransaction is not null;
        return context.Database.SqlQuery<SqlNameRow>($"SELECT Id, Name FROM Rows WHERE Name = {request.Name}");
    }
}

public sealed class TimeoutAtomicWriter : IRepositoryAtomicWriter
{
    public int? Timeout { get; private set; }

    public Task<CommandResult<TEntity>> InsertIfAbsentAsync<TEntity>(DbContext context, TEntity entity, IReadOnlyList<string> columns, IReadOnlyList<string> conflictColumns, CancellationToken cancellationToken = default) where TEntity : class
    {
        Timeout = context.Database.GetCommandTimeout();
        return new SqliteAtomicWriter().InsertIfAbsentAsync(context, entity, columns, conflictColumns, cancellationToken);
    }
}

public sealed class TimeoutReturningWriter : IRepositoryReturningWriter
{
    public int? Timeout { get; private set; }

    public Task<IReadOnlyList<TEntity>> ExecuteAsync<TEntity>(DbContext context, IQueryable source, ReturningWriteKind kind, IReadOnlyList<ReturningWriteAssignment> assignments, CancellationToken cancellationToken = default) where TEntity : class
    {
        Timeout = context.Database.GetCommandTimeout();
        return new SqliteReturningWriter().ExecuteAsync<TEntity>(context, source, kind, assignments, cancellationToken);
    }
}

public sealed class TimeoutOwnedContext(DbContextOptions<CommandContext> options) : CommandContext(options)
{
    public int? TimeoutAtDisposal { get; private set; }

    public override ValueTask DisposeAsync()
    {
        TimeoutAtDisposal = Database.GetCommandTimeout();
        return base.DisposeAsync();
    }
}