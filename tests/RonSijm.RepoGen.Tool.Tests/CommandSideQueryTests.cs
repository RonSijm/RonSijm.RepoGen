using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CommandSideQueryTests
{
    private const string Persistence = "CommandReads.Persistence";
    private const string Contracts = "CommandReads.Contracts";

    [Theory]
    [InlineData("closed", RepositoryOutputLayout.SingleFile)]
    [InlineData("closed", RepositoryOutputLayout.PerEntity)]
    [InlineData("closed", RepositoryOutputLayout.PerContract)]
    [InlineData("cqrs", RepositoryOutputLayout.SingleFile)]
    [InlineData("cqrs", RepositoryOutputLayout.PerEntity)]
    public void SelectedCapabilitiesCompileInEverySupportedLayout(string surface, RepositoryOutputLayout layout)
    {
        using var context = Context();
        var model = Model(context, surface, entity =>
        {
            entity.HasQuery("Find").ProjectTo(row => new ClosedRowDto(row.Id, row.Name)).List();
            entity.HasQuery("LoadForUpdate").OnWriteRepository().Where<int>((row, id) => row.Id == id)
                .ProjectTo(row => new ClosedRowDto(row.Id, row.Name)).SingleOrDefault();
            entity.HasQuery("Related").From(sources => sources.Set<ClosedRow>().Where(row => sources.Set<ClosedOther>().Any(other => other.Id == row.Id)))
                .Select(row => new ClosedRowDto(row.Id, row.Name)).OnWriteRepository().List();
        });
        var bundle = RepositoryGenerator.Generate(model, layout);
        bundle.Files.Should().Equal(RepositoryGenerator.Generate(model, layout).Files);
        var (assembly, _) = CompileFiles(bundle.Files.Where(static file => file.Kind != RepositoryOutputKind.Manifest).Select(static file => file.Contents));
        var read = Port(assembly, surface, write: false);
        var write = Port(assembly, surface, write: true);
        Methods(read).Should().Contain("FindAsync").And.NotContain("LoadForUpdateAsync").And.NotContain("RelatedAsync");
        Methods(write).Should().Contain("LoadForUpdateAsync").And.Contain("RelatedAsync").And.NotContain("FindAsync");
        model.Entities.Single().Queries.Single(static query => query.MethodName == "RelatedAsync").Source!.EntityTypeNames
            .Should().Contain(RepositoryModelFactory.CSharpTypeName(typeof(ClosedOther)));
    }

    [Theory]
    [InlineData("closed", false)]
    [InlineData("closed", true)]
    [InlineData("cqrs", false)]
    [InlineData("cqrs", true)]
    public void ConsumersCanCallCommandReadsOnlyThroughWritePorts(string surface, bool readConsumer)
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(Model(context, surface,
            entity => entity.HasQuery("Load").OnWriteRepository().Select(row => row.Name).List()));
        var (assembly, image) = CompileFiles(bundle.Files.Where(static file => file.Kind != RepositoryOutputKind.Manifest).Select(static file => file.Contents));
        var port = Port(assembly, surface, write: !readConsumer);
        var source = $$"""
                       public static class Consumer
                       {
                           public static void Use(global::{{port.FullName}} repository) { _ = repository.LoadAsync(); }
                       }
                       """;
        var errors = ConsumerErrors(image, source);
        if (readConsumer)
        {
            errors.Should().ContainSingle().Which.Id.Should().Be("CS1061");
        }
        else
        {
            errors.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(RepositoryTrackingBehavior.Unspecified, 1)]
    [InlineData(RepositoryTrackingBehavior.Tracking, 1)]
    [InlineData(RepositoryTrackingBehavior.NoTracking, 0)]
    [InlineData(RepositoryTrackingBehavior.NoTrackingWithIdentityResolution, 0)]
    public async Task CommandReadDefaultsUseWritePolicyNotReadOnlyPolicy(RepositoryTrackingBehavior tracking, int tracked)
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, "cqrs", entity =>
        {
            entity.HasQuery("ReadOne").Where<int>((row, id) => row.Id == id).SingleOrDefault();
            entity.HasQuery("Load").OnWriteRepository().Where<int>((row, id) => row.Id == id).SingleOrDefault();
        }, options => options.UseDefaultTracking(tracking).UseReadOnlyTracking(RepositoryTrackingBehavior.NoTracking));
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        await using var provider = Provider(assembly, context, "cqrs");
        var read = provider.GetRequiredService(Port(assembly, "cqrs", write: false));
        var write = provider.GetRequiredService(Port(assembly, "cqrs", write: true));
        (await Call<Task<ClosedRow?>>(read, "ReadOneAsync", 1, CancellationToken.None))!.Name.Should().Be("Ada");
        context.ChangeTracker.Entries().Should().BeEmpty();
        var row = (await Call<Task<ClosedRow?>>(write, "LoadAsync", 1, CancellationToken.None))!;
        context.ChangeTracker.Entries().Should().HaveCount(tracked);
        row.Name = "A command edit";
        (await Call<Task<int>>(write, "SaveChangesAsync", CancellationToken.None)).Should().Be(tracked);
        context.ChangeTracker.Clear();
        (await context.Rows.AsNoTracking().SingleAsync(item => item.Id == 1)).Name.Should().Be(tracked == 0 ? "Ada" : "A command edit");
        context.Database.CurrentTransaction.Should().BeNull();
        context.DisposeCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitTrackingAndMultiSourceRootsAlsoApplyToCommandReads(bool from)
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, "cqrs", entity =>
        {
            var query = entity.HasQuery("Load").OnWriteRepository().AsTracking();
            if (from)
            {
                query.From(sources => sources.Set<ClosedRow>().Where(row => row.Id == 1)).Single();
            }
            else
            {
                query.Where(row => row.Id == 1).Single();
            }

            entity.HasQuery("Detached").OnWriteRepository().AsReadOnly().List();
        }, options => options.UseDefaultTracking(RepositoryTrackingBehavior.NoTracking));
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        await using var provider = Provider(assembly, context, "cqrs");
        var write = provider.GetRequiredService(Port(assembly, "cqrs", write: true));
        await Call<Task<IReadOnlyList<ClosedRow>>>(write, "DetachedAsync", CancellationToken.None);
        context.ChangeTracker.Entries().Should().BeEmpty();
        (await Call<Task<ClosedRow>>(write, "LoadAsync", CancellationToken.None)).Id.Should().Be(1);
        context.ChangeTracker.Entries().Should().ContainSingle();
    }

    [Theory]
    [InlineData("closed", true)]
    [InlineData("closed", false)]
    [InlineData("cqrs", true)]
    [InlineData("cqrs", false)]
    public void DisabledCapabilitiesOmitOnlyTheirQueriesBeforeResolvingUnusedConfiguration(string surface, bool writesOnly)
    {
        using var context = Context();
        var model = Model(context, surface, entity =>
        {
            entity.ReadEnabled = !writesOnly;
            entity.WriteEnabled = writesOnly;
            entity.HasQuery("Read").Select(row => row.Name).List();
            entity.HasQuery("Write").OnWriteRepository().Select(row => row.Name).List();
            // These deliberately invalid references are on a disabled capability, not generation inputs.
            entity.HasQuery("Unused").OnWriteRepository(!writesOnly).UseLoadProfile("Missing").ProjectTo("Missing").Cache().List();
        });
        model.Entities.Single().Queries.Select(static query => query.MethodName).Should().Equal(writesOnly ? "WriteAsync" : "ReadAsync");
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        Methods(Port(assembly, surface, write: writesOnly)).Should().Contain(writesOnly ? "WriteAsync" : "ReadAsync");
        assembly.GetType((surface == "closed" ? Contracts : Persistence) + ".I" + (writesOnly ? "Read" : "Write") + "ClosedRowRepository").Should().BeNull();
    }

    [Fact]
    public async Task ExactOverloadedApplicationWritePortBindsBothReadsAndCommands()
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, "closed", entity =>
        {
            entity.ReadEnabled = false;
            entity.BindWritePort<ICommandSidePort>();
            entity.HasProjection(row => new ClosedRowDto(row.Id, row.Name));
            entity.HasQuery("Load").OnWriteRepository().Where<CommandSideId>((row, request) => row.Id == request.Value).ProjectTo<ClosedRowDto>().SingleOrDefault();
            entity.HasQuery("Load").OnWriteRepository().Where<CommandSideName>((row, request) => row.Name == request.Value).ProjectTo<ClosedRowDto>().SingleOrDefault();
            entity.HasCommand("Rename").Where<CommandSideRename>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string, CommandSideRename>(row => row.Name, (row, request) => request.Name)).Save();
        });
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        await using var provider = Provider(assembly, context, "closed");
        var port = provider.GetRequiredService<ICommandSidePort>();
        port.Should().BeSameAs(provider.GetRequiredService(Port(assembly, "closed", write: true)));
        (await port.LoadAsync(new CommandSideId(1)))!.Name.Should().Be("Ada");
        (await port.LoadAsync(new CommandSideName(null))).Should().BeNull();
        (await port.LoadAsync(new CommandSideName("Zoe")))!.Id.Should().Be(2);
        (await port.RenameAsync(new CommandSideRename(1, "Updated"))).Status.Should().Be(CommandStatus.Applied);
        (await port.LoadAsync(new CommandSideId(1)))!.Name.Should().Be("Updated");
        ClosedRepositorySourceRenderer.RenderContracts(model).Should().Contain("ICommandSidePort").And.NotContain("LoadAsync(");
        var snapshot = RepositoryApiSnapshot.Capture(model);
        snapshot.Contracts.Single(contract => contract.Type.EndsWith(".IWriteClosedRowRepository", StringComparison.Ordinal)).Methods
            .Should().HaveCount(3).And.Contain(method => method.Signature.Contains(nameof(CommandSideName), StringComparison.Ordinal));
        context.DisposeCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortBindingDoesNotStealQueriesFromOtherCapability(bool bindRead)
    {
        using var context = Context();
        Action generate = () => Model(context, "closed", entity =>
        {
            entity.HasQuery("Load").OnWriteRepository(bindRead).Where<CommandSideId>((row, request) => row.Id == request.Value)
                .ProjectTo(row => new ClosedRowDto(row.Id, row.Name)).SingleOrDefault();
            if (bindRead)
            {
                entity.BindReadPort<ICommandSideReadPort>();
            }
            else
            {
                entity.BindWritePort<ICommandSideReadPort>();
            }
        });
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1103:*capability*");
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("cqrs")]
    public void QueryAndCommandOverloadIdentityIsStillSharedAcrossCapabilities(string surface)
    {
        using var context = Context();
        Action generate = () => Model(context, surface, entity =>
        {
            entity.HasQuery("Same").Count();
            entity.HasQuery("SameAsync").OnWriteRepository().Count();
        });
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1004:*");
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("cqrs")]
    public void EnabledCommandReadCachingIsRejected(string surface)
    {
        using var context = Context();
        Action generate = () => Model(context, surface,
            entity => entity.HasQuery("Load").OnWriteRepository().Cache().Select(row => row.Name).List(),
            options =>
            {
                options.GenerateCacheDecorator();
                if (surface == "closed")
                {
                    options.UseClosedCaching(singleContextScope: "test");
                }
            });
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2103:*cannot enable caching*");
    }

    [Fact]
    public async Task DecoratedCommandReadsExecuteWithoutCacheHitsAndFluentWritesStillInvalidate()
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, "cqrs", entity =>
        {
            entity.HasQuery("Load").OnWriteRepository().Where<int>((row, id) => row.Id == id).Single();
            entity.HasQuery("Names").Cache().Select(row => row.Name).List();
        }, options => options.GenerateCacheDecorator().UseDefaultTracking(RepositoryTrackingBehavior.Tracking));
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var cache = DispatchProxy.Create(assembly.GetType(Persistence + ".IRepositoryCacheProvider")!, typeof(CqrsCacheProxy));
        await using var provider = Provider(assembly, context, "cqrs", cache);
        var read = provider.GetRequiredService(Port(assembly, "cqrs", write: false));
        var write = provider.GetRequiredService(Port(assembly, "cqrs", write: true));
        await Call<Task<IReadOnlyList<string>>>(read, "NamesAsync", CancellationToken.None);
        ((CqrsCacheProxy)cache).FactoryCalls.Should().Be(1);
        var first = await Call<Task<ClosedRow>>(write, "LoadAsync", 1, CancellationToken.None);
        context.ChangeTracker.Entries().Should().ContainSingle();
        context.ChangeTracker.Clear();
        await context.Rows.Where(row => row.Id == 1).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, "Changed elsewhere"));
        var second = await Call<Task<ClosedRow>>(write, "LoadAsync", 1, CancellationToken.None);
        second.Should().NotBeSameAs(first);
        second.Name.Should().Be("Changed elsewhere");
        ((CqrsCacheProxy)cache).FactoryCalls.Should().Be(1);
        second.Name = "Saved command";
        (await Call<Task<int>>(write, "SaveChangesAsync", CancellationToken.None)).Should().Be(1);
        ((CqrsCacheProxy)cache).Invalidations.Should().BeGreaterThan(0);
        (await Call<Task<IReadOnlyList<string>>>(read, "NamesAsync", CancellationToken.None)).Should().Contain("Saved command");
    }

    [Fact]
    public void MovingQueryToWritePortIsAnObservableBreakingReadContractChange()
    {
        using var context = Context();
        var before = Model(context, "closed", entity => entity.HasQuery("Load").Select(row => row.Name).List());
        var after = Model(context, "closed", entity => entity.HasQuery("Load").Select(row => row.Name).OnWriteRepository().List());
        var snapshot = RepositoryApiSnapshot.Capture(after);
        var changes = snapshot.CompareTo(RepositoryApiSnapshot.Capture(before));
        changes.Should().Contain(change => change.DiagnosticId == "RGA1003" && change.Contract.Contains("IRead", StringComparison.Ordinal));
        changes.Should().Contain(change => change.DiagnosticId == "RGA1007" && change.Contract.Contains("IWrite", StringComparison.Ordinal));
        snapshot.CompareTo(RepositoryApiSnapshot.Capture(before), RepositoryApiCompatibilityMode.Consumers)
            .Should().ContainSingle().Which.DiagnosticId.Should().Be("RGA1003");
        var roundtrip = RepositoryApiSnapshot.Parse(snapshot.ToJson());
        roundtrip.ToJson().Should().Be(snapshot.ToJson());
        using var manifest = JsonDocument.Parse(RepositoryManifestRenderer.Render(after));
        manifest.RootElement.GetProperty("entities")[0].GetProperty("queries")[0].GetProperty("repositoryTarget").GetString().Should().Be("write");
        RepositoryManifestRenderer.Render(before).Should().NotContain("repositoryTarget");
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("cqrs")]
    [InlineData("selectors")]
    public void ResettingPlacementRestoresByteIdenticalDefaultOutput(string surface)
    {
        using var context = Context();
        var before = RepositoryGenerator.Generate(Model(context, surface, entity => entity.HasQuery("Rows").Select(row => row.Name).List()));
        var reset = RepositoryGenerator.Generate(Model(context, surface, entity => entity.HasQuery("Rows").OnWriteRepository()
            .Select(row => row.Name).OnWriteRepository(false).List()));
        reset.Files.Should().Equal(before.Files);
    }

    [Fact]
    public void StaleResultBuilderCannotChangeAnotherStagesQueryPlacement()
    {
        var builder = new RepositoryModelBuilder();
        var original = builder.Entity<ClosedRow>().HasQuery("Rows").Select(row => row);
        original.Select(row => row.Name);
        Action change = () => original.OnWriteRepository();
        change.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
        builder.Build().Entities.Single().Queries.Single().Target.Should().Be(RepositoryQueryTarget.Read);
    }

    [Fact]
    public void UnsupportedCompatibilityPlacementAndKeylessWriteQueriesHaveSpecificDiagnostics()
    {
        using var context = Context();
        Action combined = () => Model(context, "selectors", entity => entity.HasQuery("Load").OnWriteRepository().Count());
        combined.Should().Throw<InvalidOperationException>().WithMessage("RGD2101:*");
        var builder = Builder("closed");
        builder.Entity<ClosedView>().HasQuery("Load").OnWriteRepository().Count();
        Action keyless = () => Create(context, builder);
        keyless.Should().Throw<InvalidOperationException>().WithMessage("RGD2102:*");
        builder.Entity<ClosedView>().WriteEnabled = false;
        Create(context, builder).Entities.Single().Queries.Should().BeEmpty();
    }

    [Fact]
    public void ClosedCommandReadBoundaryRejectsEntityAndOrmInputLeaksEvenWithReadsDisabled()
    {
        using var context = Context();
        Action mappedResult = () => Model(context, "closed", entity =>
        {
            entity.ReadEnabled = false;
            entity.HasQuery("Load").OnWriteRepository().List();
        });
        mappedResult.Should().Throw<InvalidOperationException>().WithMessage("RGD1502:*");
        Action ormInput = () => Model(context, "closed", entity =>
        {
            entity.ReadEnabled = false;
            entity.HasQuery("Load").OnWriteRepository().Where<ClosedRow>((row, request) => row.Id == request.Id).Count();
        });
        ormInput.Should().Throw<InvalidOperationException>().WithMessage("RGD1502:*");
    }

    [Fact]
    public async Task CommandReadRequestsAreValidatedBeforeAcquisitionAndUseFixedUnitOfWorkKey()
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, "closed", entity =>
        {
            entity.ReadEnabled = false;
            entity.HasQuery("Load").OnWriteRepository().UseContextKey<CommandSideKey, string>(request => request.Database)
                .Validate<CommandSideKey>(request => request.Id > 0, "A positive ID is required.")
                .Where<CommandSideKey>((row, request) => row.Id == request.Id).Select(row => row.Name).SingleOrDefault();
        }, options => options.UseKeyedContextLeases<string>());
        var provider = new KeyedProvider(context);
        await using var work = new KeyedRepositoryUnitOfWork<ClosedContext, string>(provider);
        var operations = Operations(ClosedRepositoryTests.Compile(model, rejectWarnings: true), work);
        Func<Task> invalid = async () => await Call<Task<string?>>(operations, "LoadAsync", new CommandSideKey("A", 0), CancellationToken.None);
        await invalid.Should().ThrowAsync<ArgumentException>();
        provider.Keys.Should().BeEmpty();
        (await Call<Task<string?>>(operations, "LoadAsync", new CommandSideKey("A", 1), CancellationToken.None)).Should().Be("Ada");
        Func<Task> switchKey = async () => await Call<Task<string?>>(operations, "LoadAsync", new CommandSideKey("B", 1), CancellationToken.None);
        await switchKey.Should().ThrowAsync<InvalidOperationException>().WithMessage("*switch context keys*");
        provider.Keys.Should().Equal("A");
    }

    [Fact]
    public void CommandReadsRequireContextKeysEvenWhenReadCapabilityIsDisabled()
    {
        using var context = Context();
        Action missing = () => Model(context, "closed", entity =>
        {
            entity.ReadEnabled = false;
            entity.HasQuery("Load").OnWriteRepository().Count();
        }, options => options.UseKeyedContextLeases<string>());
        missing.Should().Throw<InvalidOperationException>().WithMessage("RGD1202:*Load*UseContextKey*");
    }

    [Fact]
    public async Task OwnedCommandReadStreamsDisposeTheirLeaseOnEarlyStopAndCancellation()
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, "closed", entity =>
        {
            entity.ReadEnabled = false;
            entity.HasQuery("Load").OnWriteRepository().OrderBy(row => row.Id).Select(row => row.Name).Stream();
        }, options => options.UseAsyncContextLeases());
        var source = new OwnedProvider(context);
        var operations = Operations(ClosedRepositoryTests.Compile(model, rejectWarnings: true), source);
        var stream = Call<IAsyncEnumerable<string>>(operations, "LoadAsync", CancellationToken.None);
        source.Contexts.Should().BeEmpty();
        await using (var enumerator = stream.GetAsyncEnumerator())
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            enumerator.Current.Should().Be("Ada");
            source.Contexts.Single().DisposeCount.Should().Be(0);
        }

        source.Contexts.Single().DisposeCount.Should().Be(1);
        using var cancellation = new CancellationTokenSource();
        await using var canceled = Call<IAsyncEnumerable<string>>(operations, "LoadAsync", cancellation.Token).GetAsyncEnumerator();
        (await canceled.MoveNextAsync()).Should().BeTrue();
        cancellation.Cancel();
        Func<Task> move = async () => { await canceled.MoveNextAsync(); };
        await move.Should().ThrowAsync<OperationCanceledException>();
        source.Contexts.Should().HaveCount(2).And.OnlyContain(item => item.DisposeCount == 1);
    }

    [Fact]
    public void ActiveCommandReadReviewUsesCorrectDesignWhenReadQueriesAreOmitted()
    {
        using var context = new LoadingContext(new DbContextOptionsBuilder<LoadingContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder("closed").Configure(options => options.RequireNavigationVisibilityReview());
        var entity = builder.Entity<LoadingOrder>();
        entity.ReadEnabled = false;
        entity.HasQuery("Unused").Select(order => order.Name).List();
        var active = entity.HasQuery("Load").OnWriteRepository().Select(order => order.Customer == null ? string.Empty : order.Customer.Name).List();
        builder.Entity<LoadingCustomer>().HasVisibilityPolicy("Live", customer => customer.Name != "hidden");
        Action unreviewed = () => RepositoryModelFactory.Create(context, typeof(LoadingContext).Assembly, true, builder.Build());
        unreviewed.Should().Throw<InvalidOperationException>().WithMessage("RGD2001:*LoadAsync*Live*");
        active.ReviewNavigationVisibility<LoadingCustomer>("Live", "Historical names are intentional command inputs.");
        var model = RepositoryModelFactory.Create(context, typeof(LoadingContext).Assembly, true, builder.Build());
        model.NavigationVisibility!.Single().Operation.Should().StartWith("LoadAsync(");
        model.NavigationVisibility!.Single().Accesses.Single().ReviewScope.Should().Be("query");
        ClosedRepositoryTests.Compile(model, rejectWarnings: true);
    }

    [Fact]
    public void ClosedWriteReadContractsCompileWithoutEfOrPersistenceReferences()
    {
        using var context = Context();
        var model = Model(context, "closed", entity => entity.HasQuery("Load").OnWriteRepository().Where<int>((row, id) => row.Id == id)
            .ProjectTo(row => new ClosedRowDto(row.Id, row.Name)).SingleOrDefault());
        var sources = new[]
        {
            ClosedRepositorySourceRenderer.RenderContracts(model),
            "namespace RonSijm.RepoGen.Tool.Tests { public sealed record ClosedRowDto(int Id, string Name); }",
            "public class Consumer { public void Use(CommandReads.Contracts.IWriteClosedRowRepository write) { _ = write.LoadAsync(1); } }"
        };
        var compilation = CSharpCompilation.Create("PureWriteReadContracts", sources.Select(static source => CSharpSyntaxTree.ParseText(source)),
            ClosedRepositoryTests.PlatformReferences().Append(MetadataReference.CreateFromFile(typeof(CommandResult).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Bad { void Use(CommandReads.Contracts.IReadClosedRowRepository read) { _ = read.LoadAsync(1); } }"))
            .GetDiagnostics().Should().Contain(static diagnostic => diagnostic.Id == "CS1061");
    }

    [Fact]
    public async Task WriteOnlyQueriesExpandReusableInputAndNamedProjectionAdapters()
    {
        await using var context = Context();
        await SeedAsync(context);
        var builder = Builder("closed");
        var id = builder.HasTypeAdapter<int, CommandSideId>(value => new CommandSideId(value), request => request.Value);
        var dto = builder.HasTypeAdapter<ClosedRow, ClosedRowDto>(row => new ClosedRowDto(row.Id, row.Name), value => new ClosedRow { Id = value.Id, Name = value.Name });
        var entity = builder.Entity<ClosedRow>();
        entity.ReadEnabled = false;
        entity.HasProjection(dto);
        entity.HasQuery("Load").OnWriteRepository().Where<CommandSideId>((row, request) => row.Id == id.ToModel(request))
            .ProjectTo<ClosedRowDto>().Results<ClosedRowDto>().Select(value => value.Name).SingleOrDefault();
        var model = Create(context, builder);
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        await using var provider = Provider(assembly, context, "closed");
        (await Call<Task<string?>>(provider.GetRequiredService(Port(assembly, "closed", write: true)), "LoadAsync", new CommandSideId(1), CancellationToken.None))
            .Should().Be("Ada");
        RepositorySourceRenderer.Render(model).Should().NotContain(".ToModel(").And.NotContain(".ToContract(");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommandLoadProfilesReturnClosedDtosOrTrackedCqrsGraphs(bool closed)
    {
        await using var context = new LoadingContext(new DbContextOptionsBuilder<LoadingContext>().UseSqlite("Data Source=:memory:").Options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.Orders.Add(new LoadingOrder { Name = "One", Lines = [new LoadingLine { Quantity = 7, Product = new LoadingProduct { Name = "Tea" } }] });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = Builder(closed ? "closed" : "cqrs");
        var entity = builder.Entity<LoadingOrder>();
        entity.ReadEnabled = false;
        entity.HasLoadProfile("Change", load => load.Include(order => order.Lines).ThenInclude(line => line.Product));
        var query = entity.HasQuery("Load").OnWriteRepository().UseLoadProfile("Change").AsTracking().Where<int>((order, id) => order.Id == id);
        if (closed)
        {
            query.ProjectTo(order => LoadingMapper.LinesOnly(order)).Single();
        }
        else
        {
            query.Single();
        }

        var model = RepositoryModelFactory.Create(context, typeof(LoadingContext).Assembly, true, builder.Build());
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var operations = Activator.CreateInstance(assembly.GetType(Persistence + (closed ? ".LoadingOrderRepositoryOperations" : ".LoadingOrderRepository"))!, context)!;
        if (closed)
        {
            var dto = await Call<Task<LoadingOrderDto>>(operations, "LoadAsync", 1, CancellationToken.None);
            dto.Lines.Single().Product.Should().Be("Tea");
            dto.Lines.Single().Quantity.Should().Be(7);
            ClosedRepositorySourceRenderer.RenderContracts(model).Should().NotContain("LoadingOrder>").And.NotContain("Selector");
        }
        else
        {
            var graph = await Call<Task<LoadingOrder>>(operations, "LoadAsync", 1, CancellationToken.None);
            graph.Lines.Single().Product.Name.Should().Be("Tea");
            graph.Lines.Single().Quantity = 8;
            (await Call<Task<int>>(operations, "SaveChangesAsync", CancellationToken.None)).Should().Be(1);
        }

        context.ChangeTracker.Entries().Should().HaveCount(3);
        context.DisposeCount.Should().Be(0);
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("cqrs")]
    public async Task CommandReadsHonorRootVisibilityUnlessDesignerExplicitlyBypassesIt(string surface)
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, surface, entity =>
        {
            entity.HasVisibilityPolicy("Visible", row => row.Name == "Ada");
            entity.HasQuery("Load").OnWriteRepository().Select(row => row.Name).List();
            entity.HasQuery("History").OnWriteRepository().BypassVisibility("Visible").Count();
        });
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        await using var provider = Provider(assembly, context, surface);
        var write = provider.GetRequiredService(Port(assembly, surface, write: true));
        (await Call<Task<IReadOnlyList<string>>>(write, "LoadAsync", CancellationToken.None)).Should().Equal("Ada");
        (await Call<Task<int>>(write, "HistoryAsync", CancellationToken.None)).Should().Be(2);
    }

    [Fact]
    public async Task CommandReadsUseTheSameTransactionAsSavingAndSetBasedWrites()
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, "closed", entity =>
        {
            entity.ReadEnabled = false;
            entity.HasQuery("Count").OnWriteRepository().Count();
            entity.HasCommand("Create").Create<CommandSideName>(request => new ClosedRow { Name = request.Value ?? "" }).Save();
            entity.HasCommand("Delete").Where<int>((row, id) => row.Id == id).Delete();
        }, options => options.UseAsyncContextLeases());
        await using var work = new RepositoryUnitOfWork<ClosedContext>(context);
        var operations = Operations(ClosedRepositoryTests.Compile(model, rejectWarnings: true), work);
        await using (var transaction = await work.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
        {
            (await Call<Task<CommandResult>>(operations, "CreateAsync", new CommandSideName("Transactional"), CancellationToken.None)).Status.Should().Be(CommandStatus.Applied);
            (await Call<Task<int>>(operations, "CountAsync", CancellationToken.None)).Should().Be(3);
            (await Call<Task<int>>(operations, "DeleteAsync", 1, CancellationToken.None)).Should().Be(1);
            (await Call<Task<int>>(operations, "CountAsync", CancellationToken.None)).Should().Be(2);
            await transaction.RollbackAsync();
        }

        (await context.Rows.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Name).ToListAsync()).Should().Equal("Ada", "Zoe");
        context.Database.CurrentTransaction.Should().BeNull();
    }

    [Fact]
    public void DataSnapshotsCaptureOnlyEnabledCommandReadInputAndResultTypes()
    {
        using var context = Context();
        var model = Model(context, "closed", entity =>
        {
            entity.ReadEnabled = false;
            entity.HasQuery("Unused").Where<CommandSideName>((row, request) => row.Name == request.Value).Count();
            entity.HasQuery("Load").OnWriteRepository().Where<CommandSideId>((row, request) => row.Id == request.Value)
                .ProjectTo(row => new ClosedRowDto(row.Id, row.Name)).SingleOrDefault();
        }, options => options.IncludeApiDataContracts());
        model.DataContracts!.Select(static contract => contract.Type).Should().Contain("global::" + typeof(CommandSideId).FullName)
            .And.Contain("global::" + typeof(ClosedRowDto).FullName).And.NotContain("global::" + typeof(CommandSideName).FullName);
        RepositoryApiSnapshot.Capture(model).Contracts.Single(contract => contract.Type.EndsWith(".IWriteClosedRowRepository", StringComparison.Ordinal))
            .Methods.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledQueriesDoNotRequireContextKeyOptionsOrValidateSorting(bool write)
    {
        using var context = new LoadingContext(new DbContextOptionsBuilder<LoadingContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Builder("closed");
        var entity = builder.Entity<LoadingOrder>();
        entity.ReadEnabled = write;
        entity.WriteEnabled = !write;
        entity.HasQuery("Unused").OnWriteRepository(write).UseContextKey<CommandSideKey, string>(request => request.Database)
            .AllowSorting(order => order.Unmapped).Count();
        entity.HasQuery("Active").OnWriteRepository(!write).Count();
        var model = RepositoryModelFactory.Create(context, typeof(LoadingContext).Assembly, true, builder.Build());
        model.Entities.Single().SortableFields.Should().BeEmpty();
        model.Entities.Single().Queries.Should().ContainSingle().Which.MethodName.Should().Be("ActiveAsync");
        ClosedRepositoryTests.Compile(model, rejectWarnings: true);
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("cqrs")]
    public void CommandReadCannotMasqueradeAsSaveChanges(string surface)
    {
        using var context = Context();
        Action generate = () => Model(context, surface, entity => entity.HasQuery("SaveChanges").OnWriteRepository().Count());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2104:*save operation*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CqrsWriteHelperCollisionsAreDiagnosedBeforeSourcePublication(bool batch)
    {
        using var context = Context();
        Action generate = () => Model(context, "cqrs", entity =>
        {
            if (batch)
            {
                entity.HasQuery("AddRange").OnWriteRepository().Where<IEnumerable<ClosedRow>>((row, request) => row.Id > 0).Count();
            }
            else
            {
                entity.HasQuery("Add").OnWriteRepository().Where<ClosedRow>((row, request) => row.Id == request.Id).Count();
            }
        }, options => options.GenerateBatchOperations(batch));
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2104:*write helper*");
        if (batch)
        {
            // The optional helper is absent; the operation name is no longer reserved.
            var model = Model(context, "cqrs", entity => entity.HasQuery("AddRange").OnWriteRepository()
                .Where<IEnumerable<ClosedRow>>((row, request) => row.Id > 0).Count());
            ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        }
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("cqrs")]
    public async Task CommandReadResultPipelinesKeepGroupedPagedAndKeyedTerminalShapes(string surface)
    {
        await using var context = Context();
        await SeedAsync(context);
        var model = Model(context, surface, entity =>
        {
            // Member initialization permits provider-side access to projected columns in later clauses.
            entity.HasQuery("Page").Select(row => new SqlNameRow { Id = row.Id, Name = row.Name }).OrderBy(row => row.Id).OnWriteRepository().Paged();
            entity.HasQuery("Slice").Select(row => new SqlNameRow { Id = row.Id, Name = row.Name }).OrderBy(row => row.Id).OnWriteRepository().Slice();
            entity.HasQuery("Dictionary").Select(row => new SqlNameRow { Id = row.Id, Name = row.Name }).OnWriteRepository().Dictionary(row => row.Id, row => row.Name);
            entity.HasQuery("Totals").GroupBy(row => row.Name.Length, group => new SqlNameRow { Id = group.Count(), Name = "Grouped" })
                .Results<SqlNameRow>().OnWriteRepository().OrderBy(row => row.Id).List();
            entity.HasQuery("Sum").Select(row => row.Id).OnWriteRepository().Sum(value => value);
            entity.HasQuery("Cursor").Select(row => new SqlNameRow { Id = row.Id, Name = row.Name }).OrderBy(row => row.Id).OnWriteRepository()
                .CursorPaged<int>((row, cursor) => row.Id > cursor, row => row.Id);
        });
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        await using var provider = Provider(assembly, context, surface);
        var write = provider.GetRequiredService(Port(assembly, surface, write: true));
        var page = await UntypedResult(Call<object>(write, "PageAsync", 2, 1, CancellationToken.None));
        ((IReadOnlyList<SqlNameRow>)Property(page, "Items")).Single().Name.Should().Be("Zoe");
        Property(page, "TotalCount").Should().Be(2);
        var slice = await UntypedResult(Call<object>(write, "SliceAsync", 1, 1, CancellationToken.None));
        ((IReadOnlyList<SqlNameRow>)Property(slice, "Items")).Single().Name.Should().Be("Ada");
        Property(slice, "HasMore").Should().Be(true);
        (await Call<Task<IReadOnlyDictionary<int, string>>>(write, "DictionaryAsync", CancellationToken.None)).Should().BeEquivalentTo(new Dictionary<int, string> { [1] = "Ada", [2] = "Zoe" });
        var total = (await Call<Task<IReadOnlyList<SqlNameRow>>>(write, "TotalsAsync", CancellationToken.None)).Should().ContainSingle().Which;
        total.Id.Should().Be(2);
        total.Name.Should().Be("Grouped");
        (await Call<Task<int>>(write, "SumAsync", CancellationToken.None)).Should().Be(3);
        object request = surface == "closed"
            ? new CursorPageRequest<int>(1)
            : assembly.GetType(Persistence + ".CursorPageRequest`1")!
                .MakeGenericType(typeof(int)).GetMethod("First")!.Invoke(null, [1])!;
        var cursor = await UntypedResult(Call<object>(write, "CursorAsync", request, CancellationToken.None));
        ((IReadOnlyList<SqlNameRow>)Property(cursor, "Items")).Single().Id.Should().Be(1);
        Property(cursor, "HasMore").Should().Be(true);
    }

    private static object Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)!;

    private static async Task<object> UntypedResult(object value)
    {
        await (Task)value;
        return Property(value, "Result");
    }

    private static RepositoryModelBuilder Builder(string surface)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities().UseGeneratedNamespace(Persistence));
        if (surface == "closed")
        {
            builder.Configure(options => options.UseClosedRepositories(Contracts));
        }

        if (surface == "cqrs")
        {
            builder.Configure(options => options.UseCqrsSelectors());
        }

        return builder;
    }

    private static RepositoryGenerationModel Model(ClosedContext context, string surface, Action<RepositoryEntityBuilder<ClosedRow>> configure,
        Action<RepositoryGenerationOptionsBuilder>? options = null)
    {
        var builder = Builder(surface);
        if (options is not null)
        {
            builder.Configure(options);
        }

        configure(builder.Entity<ClosedRow>());
        return Create(context, builder);
    }

    private static RepositoryGenerationModel Create(ClosedContext context, RepositoryModelBuilder builder) =>
        RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, true, builder.Build());

    private static ClosedContext Context() => new(new DbContextOptionsBuilder<ClosedContext>().UseSqlite("Data Source=:memory:").Options);

    private static async Task SeedAsync(ClosedContext context)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.Rows.AddRange(new ClosedRow { Id = 1, Name = "Ada" }, new ClosedRow { Id = 2, Name = "Zoe" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static Type Port(Assembly assembly, string surface, bool write) => assembly.GetType((surface == "closed" ? Contracts : Persistence) + ".I" +
                                                                                                (write ? "Write" : "Read") + "ClosedRowRepository", throwOnError: true)!;

    private static string[] Methods(Type port) => port.GetInterfaces().Append(port).SelectMany(static type => type.GetMethods()).Select(static method => method.Name).ToArray();

    private static T Call<T>(object target, string method, params object[] arguments) => (T)target.GetType().GetMethods()
        .Single(item => item.Name == method && item.GetParameters().Length == arguments.Length && !item.IsGenericMethod).Invoke(target, arguments)!;

    private static object Operations(Assembly assembly, object source) => Activator.CreateInstance(assembly.GetType(Persistence + ".ClosedRowRepositoryOperations")!, source)!;

    private static ServiceProvider Provider(Assembly assembly, ClosedContext context, string surface, object? cache = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        if (cache is not null)
        {
            services.AddSingleton(assembly.GetType(Persistence + ".IRepositoryCacheProvider")!, cache);
        }

        var register = assembly.GetType((surface == "closed" ? Contracts + ".ClosedContextRepositoryRegistration" : Persistence + ".ClosedContextSelectorRepositoryRegistration"))!
            .GetMethods().Single(static method => method.Name.StartsWith("AddClosedContext", StringComparison.Ordinal));
        register.Invoke(null, register.GetParameters().Length == 2 ? [services, cache is not null] : [services]);
        return services.BuildServiceProvider();
    }

    private static MetadataReference[] References() => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Append(typeof(CommandSideQueryTests).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase).Select(static path => MetadataReference.CreateFromFile(path)).ToArray();

    private static (Assembly Assembly, byte[] Image) CompileFiles(IEnumerable<string> sources)
    {
        var compilation = CSharpCompilation.Create("CommandSideGenerated_" + Guid.NewGuid().ToString("N"), sources.Select(static source => CSharpSyntaxTree.ParseText(source)),
            References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error || diagnostic.Severity == DiagnosticSeverity.Warning && diagnostic.Id != "CS1701").Should().BeEmpty();
        var image = stream.ToArray();
        stream.Position = 0;
        return (AssemblyLoadContext.Default.LoadFromStream(stream), image);
    }

    private static Diagnostic[] ConsumerErrors(byte[] image, string source) => CSharpCompilation.Create("CommandSideConsumer", [CSharpSyntaxTree.ParseText(source)],
            References().Append(MetadataReference.CreateFromImage(image)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
        .GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();

    private sealed class OwnedProvider(ClosedContext source) : IDbContextLeaseProvider<ClosedContext>
    {
        public List<ClosedContext> Contexts { get; } = [];

        public ValueTask<DbContextLease<ClosedContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new ClosedContext(new DbContextOptionsBuilder<ClosedContext>().UseSqlite(source.Database.GetDbConnection()).Options);
            Contexts.Add(context);
            return ValueTask.FromResult(DbContextLease.Owned(context));
        }
    }

    private sealed class KeyedProvider(ClosedContext source) : IKeyedDbContextLeaseProvider<ClosedContext, string>
    {
        public List<string> Keys { get; } = [];

        public ValueTask<DbContextLease<ClosedContext>> AcquireAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Keys.Add(key);
            return ValueTask.FromResult(DbContextLease.Borrowed(source, key));
        }
    }
}

public sealed record CommandSideId(int Value);

public sealed record CommandSideName(string? Value);

public sealed record CommandSideRename(int Id, string Name);

public sealed record CommandSideKey(string Database, int Id);

public interface ICommandSideReadPort
{
    Task<ClosedRowDto?> LoadAsync(CommandSideId request, CancellationToken cancellationToken = default);
}

public interface ICommandSidePort : ICommandSideReadPort
{
    Task<ClosedRowDto?> LoadAsync(CommandSideName request, CancellationToken cancellationToken = default);
    Task<CommandResult> RenameAsync(CommandSideRename request, CancellationToken cancellationToken = default);
}