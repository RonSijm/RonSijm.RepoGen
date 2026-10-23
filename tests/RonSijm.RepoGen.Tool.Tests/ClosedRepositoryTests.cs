using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ClosedRepositoryTests
{
    [Fact]
    public void ContractsCompileWithoutEfAndReadConsumersCannotMutate()
    {
        using var context = CreateContext();
        var model = CreateModel(context);
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(model);
        contracts.Should().Contain("interface IReadClosedRowRepository");
        contracts.Should().Contain("interface IWriteClosedRowRepository");
        contracts.Should().NotContain("Microsoft.EntityFrameworkCore").And.NotContain("Selector").And.NotContain("DbContext");
        var references = PlatformReferences().Append(MetadataReference.CreateFromFile(typeof(PagedResult<>).Assembly.Location));
        const string dtoSource = "namespace RonSijm.RepoGen.Tool.Tests { public sealed record ClosedRowDto(int Id, string Name); }";
        var compilation = CSharpCompilation.Create("Contracts", [CSharpSyntaxTree.ParseText(contracts), CSharpSyntaxTree.ParseText(dtoSource)],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var consumer = CSharpSyntaxTree.ParseText("class Consumer { void Bad(Generated.Contracts.IReadClosedRowRepository repository) { repository.DeleteAllAsync(); } }");
        compilation.AddSyntaxTrees(consumer).GetDiagnostics().Should().Contain(static diagnostic => diagnostic.Id == "CS1061");
    }

    [Fact]
    public async Task GeneratedOperationsExecuteProjectionsPagingAndCommandsAgainstSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await SeedAsync(context);
        var provider = new RecordingLeaseProvider(context);
        var operations = CreateOperations(Compile(CreateModel(context)), provider);

        var list = await Invoke<Task<IReadOnlyList<ClosedRowDto>>>(operations, "FindAsync", "a", CancellationToken.None);
        list.Select(static row => row.Name).Should().Equal("Ada", "Grace");
        provider.Contexts.Should().ContainSingle();
        provider.Contexts[0].DisposeCount.Should().Be(1);

        (await Invoke<Task<int?>>(operations, "FirstIdAsync", -1, CancellationToken.None)).Should().BeNull();
        (await Invoke<Task<int?>>(operations, "FirstIdAsync", 1, CancellationToken.None)).Should().Be(1);

        var page = await Invoke<Task<PagedResult<ClosedRowDto>>>(operations, "PageAsync", 2, 2, CancellationToken.None);
        page.Items.Select(static row => row.Name).Should().Equal("Ron");
        page.TotalCount.Should().Be(3);
        page.Page.Should().Be(2);
        var cursor = await Invoke<Task<CursorPagedResult<ClosedRowDto, int>>>(operations, "CursorAsync", new CursorPageRequest<int>(2), CancellationToken.None);
        cursor.Items.Should().HaveCount(2);
        cursor.HasMore.Should().BeTrue();
        var next = await Invoke<Task<CursorPagedResult<ClosedRowDto, int>>>(operations, "CursorAsync", new CursorPageRequest<int>(cursor.NextCursor, 2), CancellationToken.None);
        next.Items.Should().ContainSingle().Which.Name.Should().Be("Ron");
        (await Invoke<Task<int>>(operations, "DeleteNamedAsync", "Ron", CancellationToken.None)).Should().Be(1);
        (await context.Rows.CountAsync()).Should().Be(2);
        provider.Contexts.Should().OnlyContain(static item => item.DisposeCount == 1);
    }

    [Fact]
    public async Task GeneratedOperationsDisposeOnFailureAndCancellation()
    {
        await using var context = CreateContext();
        await SeedAsync(context);
        var provider = new RecordingLeaseProvider(context);
        var operations = CreateOperations(Compile(CreateModel(context)), provider);
        Func<Task> multipleRows = () => Invoke<Task<ClosedRowDto>>(operations, "RequireOneAsync", CancellationToken.None);
        await multipleRows.Should().ThrowAsync<InvalidOperationException>();
        provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        Func<Task> cancellation = () => Invoke<Task<IReadOnlyList<ClosedRowDto>>>(operations, "FindAsync", "a", canceled.Token);
        await cancellation.Should().ThrowAsync<OperationCanceledException>();
        provider.Contexts.Should().ContainSingle();
    }

    [Fact]
    public async Task StreamingHoldsLeaseUntilEarlyDisposal()
    {
        await using var context = CreateContext();
        await SeedAsync(context);
        var provider = new RecordingLeaseProvider(context);
        var operations = CreateOperations(Compile(CreateModel(context)), provider);
        var stream = Invoke<IAsyncEnumerable<ClosedRowDto>>(operations, "StreamAsync", CancellationToken.None);
        provider.Contexts.Should().BeEmpty();
        var enumerator = stream.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        provider.Contexts.Should().ContainSingle().Which.DisposeCount.Should().Be(0);
        await enumerator.DisposeAsync();
        provider.Contexts[0].DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task RegistrationsShareImplementationAndBorrowedContextIsNotDisposed()
    {
        await using var context = CreateContext();
        await SeedAsync(context);
        var model = CreateModel(context, asyncLeases: false);
        var assembly = Compile(model);
        var services = new ServiceCollection();
        services.AddSingleton(context);
        assembly.GetType("Generated.Contracts.ClosedContextRepositoryRegistration")!.GetMethod("AddClosedContextRepositories")!.Invoke(null, [services]);
        await using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService(assembly.GetType("Generated.Contracts.IReadClosedRowRepository")!);
        var write = scope.ServiceProvider.GetRequiredService(assembly.GetType("Generated.Contracts.IWriteClosedRowRepository")!);
        read.Should().BeSameAs(write);
        (await Invoke<Task<IReadOnlyList<ClosedRowDto>>>(read, "FindAsync", "a", CancellationToken.None)).Should().HaveCount(2);
        context.DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task CommandsParticipateInSharedUnitOfWorkAndNotifyOnlyOnCommit()
    {
        await using var context = CreateContext();
        await SeedAsync(context);
        var observer = new ClosedCommitObserver();
        await using var work = new RepositoryUnitOfWork<ClosedContext>(context, [observer]);
        var operations = CreateOperations(Compile(CreateModel(context)), work);
        await using var transaction = await work.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        (await Invoke<Task<int>>(operations, "DeleteNamedAsync", "Ron", CancellationToken.None)).Should().Be(1);
        observer.Calls.Should().Be(0);
        await transaction.CommitAsync();
        observer.Calls.Should().Be(1);
        observer.Tags.Should().Contain(typeof(ClosedRow).FullName!);
        context.DisposeCount.Should().Be(0);
    }

    [Fact]
    public void ModulesAndExclusionsGenerateOnlySelectedCapabilities()
    {
        using var context = CreateContext();
        var model = RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: new ModularDesigner().CreateModel());
        model.Entities.Should().ContainSingle();
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(model);
        contracts.Should().Contain("IReadClosedRowRepository").And.NotContain("IWriteClosedRowRepository").And.NotContain("DeleteNamed").And.NotContain("RequireOne");
        contracts.Should().NotContain("ClosedOther");
        RepositorySourceRenderer.Render(model).Should().NotContain("DeleteNamedAsync");
        var manifest = RepositoryManifestRenderer.Render(model);
        manifest.Should().Be(RepositoryManifestRenderer.Render(model));
        manifest.Should().Contain("closedOperations").And.Contain("FindAsync");
    }

    [Fact]
    public async Task ExistingApplicationPortsAreBoundAndRegisteredToOneImplementation()
    {
        await using var context = CreateContext();
        await SeedAsync(context);
        var assembly = Compile(CreateModel(context, asyncLeases: false, bindPorts: true));
        var services = new ServiceCollection();
        services.AddSingleton(context);
        assembly.GetType("Generated.Contracts.ClosedContextRepositoryRegistration")!.GetMethod("AddClosedContextRepositories")!.Invoke(null, [services]);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var read = scope.ServiceProvider.GetRequiredService<IExistingRowReadPort>();
        var write = scope.ServiceProvider.GetRequiredService<IExistingRowWritePort>();
        read.Should().BeSameAs(write);
        (await read.CountRowsAsync()).Should().Be(3);
        (await read.FindAsync("a")).Should().HaveCount(2);
        (await read.FindOneAsync(-1)).Should().BeNull();
        (await write.DeleteNamedAsync("Ron")).Should().Be(1);
    }

    [Fact]
    public void KeylessEntitiesHaveNoWritePortAndMissingPortMembersAreRejected()
    {
        using var context = CreateContext();
        var keyless = RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: new KeylessDesigner().CreateModel());
        ClosedRepositorySourceRenderer.RenderContracts(keyless).Should().Contain("IReadClosedViewRepository").And.NotContain("IWriteClosedViewRepository");
        Action missing = () => RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: new IncompatiblePortDesigner(wrongNullability: false).CreateModel());
        missing.Should().Throw<InvalidOperationException>().WithMessage("*no matching configured operation*");
        Action nullable = () => RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: new IncompatiblePortDesigner(wrongNullability: true).CreateModel());
        nullable.Should().Throw<InvalidOperationException>().WithMessage("*incompatible signature or nullability*");
    }

    [Theory]
    [InlineData("entity")]
    [InlineData("nestedEntity")]
    [InlineData("cache")]
    [InlineData("duplicate")]
    [InlineData("reservedParameter")]
    [InlineData("asyncSelector")]
    public void UnsafeClosedDesignsProduceActionableDiagnostics(string scenario)
    {
        using var context = CreateContext();
        Action create = () => RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: new InvalidDesigner(scenario).CreateModel());
        create.Should().Throw<InvalidOperationException>();
    }

    private static ClosedContext CreateContext(SqliteConnection? connection = null) => new(
        connection is null
            ? new DbContextOptionsBuilder<ClosedContext>().UseSqlite("Data Source=:memory:").Options
            : new DbContextOptionsBuilder<ClosedContext>().UseSqlite(connection).Options);

    private static async Task SeedAsync(ClosedContext context)
    {
        if (context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync();
        }

        await context.Database.EnsureCreatedAsync();
        context.Rows.AddRange(new ClosedRow { Name = "Ada" }, new ClosedRow { Name = "Grace" }, new ClosedRow { Name = "Ron" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static RepositoryGenerationModel CreateModel(ClosedContext context, bool asyncLeases = true, bool bindPorts = false) =>
        RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: new ClosedDesigner(asyncLeases, bindPorts).CreateModel());

    private static object CreateOperations(Assembly assembly, IDbContextLeaseProvider<ClosedContext> provider) =>
        Activator.CreateInstance(assembly.GetType(typeof(ClosedRow).Namespace + ".ClosedRowRepositoryOperations")!, provider)!;

    private static TResult Invoke<TResult>(object target, string name, params object[] arguments) =>
        (TResult)target.GetType().GetMethod(name)!.Invoke(target, arguments)!;

    internal static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Where(static path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(path) is "netstandard.dll" or "mscorlib.dll")
        .Select(static path => MetadataReference.CreateFromFile(path));

    internal static Assembly Compile(RepositoryGenerationModel model, bool rejectWarnings = false)
    {
        var sources = new[]
        {
            RepositorySourceRenderer.Render(model), model.Settings.ContractNamespace is null
                ? ProjectionSourceRenderer.Render(model)
                : ClosedRepositorySourceRenderer.RenderContracts(model)
        };
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(ClosedRepositoryTests).Assembly.Location).Append(typeof(DbContext).Assembly.Location)
            .Append(typeof(RelationalQueryableExtensions).Assembly.Location).Append(typeof(IDbContextLeaseProvider<>).Assembly.Location)
            .Append(typeof(PagedResult<>).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create("Generated_" + Guid.NewGuid().ToString("N"), sources.Select(static source => CSharpSyntaxTree.ParseText(source)),
            paths.Select(static path => MetadataReference.CreateFromFile(path)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        // CS1701 is the harness's EF8/.NET10 reference unification, not a generated-source warning.
        var failures = result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error || rejectWarnings && diagnostic.Severity == DiagnosticSeverity.Warning && diagnostic.Id != "CS1701").ToArray();
        failures.Select(static diagnostic => diagnostic + (diagnostic.Location.IsInSource ? Environment.NewLine +
            diagnostic.Location.SourceTree!.GetText().Lines[diagnostic.Location.GetLineSpan().StartLinePosition.Line] : ""))
            .Should().BeEmpty("generated source should compile");
        output.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(output);
    }

    private sealed class RecordingLeaseProvider(ClosedContext source) : IDbContextLeaseProvider<ClosedContext>
    {
        public List<ClosedContext> Contexts { get; } = [];

        public ValueTask<DbContextLease<ClosedContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new ClosedContext(new DbContextOptionsBuilder<ClosedContext>().UseSqlite(source.Database.GetDbConnection()).Options);
            // In-memory SQLite needs the original connection shared by each operation.
            Contexts.Add(context);
            return ValueTask.FromResult(DbContextLease.Owned(context));
        }
    }

    private sealed class ClosedCommitObserver : IRepositoryCommitObserver
    {
        public int Calls { get; private set; }
        public IReadOnlyCollection<string> Tags { get; private set; } = [];

        public Task CommittedAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
        {
            Calls++;
            Tags = tags;
            return Task.CompletedTask;
        }
    }

    private sealed class ClosedDesigner(bool asyncLeases, bool bindPorts) : RepositoryDesigner
    {
        protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
        {
            modelBuilder.Configure(options => options.UseClosedRepositories("Generated.Contracts").UseAsyncContextLeases(asyncLeases).GenerateCacheDecorator(false).GenerateOnlyConfiguredEntities());
            modelBuilder.ApplyConfiguration(new RowModule());
            if (bindPorts)
            {
                modelBuilder.Entity<ClosedRow>().BindReadPort<IExistingRowReadPort>().BindWritePort<IExistingRowWritePort>();
            }
        }
    }

    private sealed class RowModule : IRepositoryEntityConfiguration<ClosedRow>
    {
        public void Configure(RepositoryEntityBuilder<ClosedRow> entity)
        {
            entity.HasProjection(row => new ClosedRowDto(row.Id, row.Name));
            entity.HasProjection(row => row.Id);
            entity.HasQuery("Find").Where<string>((row, term) => row.Name.Contains(term)).OrderBy(row => row.Id).ProjectTo<ClosedRowDto>().List();
            entity.HasQuery("FindOne").Where<int>((row, id) => row.Id == id).ProjectTo<ClosedRowDto>().SingleOrDefault();
            entity.HasQuery("CountRows").Count();
            entity.HasQuery("FirstId").Where<int>((row, id) => row.Id == id).ProjectTo<int>().FirstOrDefault();
            entity.HasQuery("Page").OrderBy(row => row.Id).ProjectTo<ClosedRowDto>().Paged();
            entity.HasQuery("Cursor").OrderBy(row => row.Id).ProjectTo<ClosedRowDto>().CursorPaged<int>((row, cursor) => row.Id > cursor, row => row.Id);
            entity.HasQuery("RequireOne").ProjectTo<ClosedRowDto>().Single();
            entity.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<ClosedRowDto>().Stream();
            entity.HasCommand("DeleteNamed").Where<string>((row, name) => row.Name == name).Delete();
        }
    }

    private sealed class ModularDesigner : RepositoryDesigner
    {
        protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
        {
            modelBuilder.Configure(options => options.UseClosedRepositories("Generated.Contracts").GenerateCacheDecorator(false));
            modelBuilder.ApplyConfiguration(new RowModule());
            modelBuilder.Entity<ClosedRow>().WriteEnabled = false;
            modelBuilder.Entity<ClosedRow>().ExcludeQuery("RequireOne");
            modelBuilder.Entity<ClosedOther>().RepositoryEnabled = false;
            modelBuilder.Entity<ClosedView>().RepositoryEnabled = false;
        }
    }

    private sealed class KeylessDesigner : RepositoryDesigner
    {
        protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
        {
            modelBuilder.Configure(options => options.UseClosedRepositories("Generated.Contracts").GenerateOnlyConfiguredEntities());
            modelBuilder.Entity<ClosedView>().HasQuery("CountRows").Count();
        }
    }

    private sealed class IncompatiblePortDesigner(bool wrongNullability) : RepositoryDesigner
    {
        protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
        {
            modelBuilder.Configure(options => options.UseClosedRepositories("Generated.Contracts").GenerateOnlyConfiguredEntities());
            if (wrongNullability)
            {
                modelBuilder.ApplyConfiguration(new RowModule());
                modelBuilder.Entity<ClosedRow>().BindReadPort<IWrongNullabilityPort>();
            }
            else
            {
                modelBuilder.Entity<ClosedRow>().BindReadPort<IExistingRowReadPort>();
            }
        }
    }

    private sealed class InvalidDesigner(string scenario) : RepositoryDesigner
    {
        protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
        {
            modelBuilder.Configure(options => options.GenerateOnlyConfiguredEntities());
            if (scenario == "asyncSelector")
            {
                modelBuilder.Configure(options => options.UseAsyncContextLeases());
            }
            else
            {
                modelBuilder.Configure(options => options.UseClosedRepositories("Generated.Contracts"));
            }

            var entity = modelBuilder.Entity<ClosedRow>();
            if (scenario == "entity")
            {
                entity.HasQuery("Invalid").List();
            }

            if (scenario == "nestedEntity")
            {
                entity.HasQuery("Invalid").ProjectTo(row => new InvalidClosedDto(row)).List();
            }

            if (scenario == "cache")
            {
                entity.HasQuery("Invalid").ProjectTo(row => new ClosedRowDto(row.Id, row.Name)).Cache().List();
            }

            if (scenario == "duplicate")
            {
                entity.HasQuery("Same").Count();
                entity.HasCommand("SameAsync").Delete();
            }

            if (scenario == "reservedParameter")
            {
                entity.HasQuery("Invalid").Where<int>((row, pageSize) => row.Id == pageSize).Count();
            }
        }
    }
}

public sealed record ClosedRowDto(int Id, string Name);

public sealed record InvalidClosedDto(ClosedRow Entity);

public interface IExistingRowCountPort
{
    Task<int> CountRowsAsync(CancellationToken cancellationToken = default);
}

public interface IExistingRowReadPort : IExistingRowCountPort
{
    Task<IReadOnlyList<ClosedRowDto>> FindAsync(string term, CancellationToken cancellationToken = default);
    Task<ClosedRowDto?> FindOneAsync(int id, CancellationToken cancellationToken = default);
}

public interface IExistingRowWritePort
{
    Task<int> DeleteNamedAsync(string name, CancellationToken cancellationToken = default);
}

public interface IWrongNullabilityPort
{
    Task<ClosedRowDto> FindOneAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class ClosedRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class ClosedOther
{
    public int Id { get; set; }
}

public sealed class ClosedView
{
    public string Name { get; set; } = string.Empty;
}

public sealed class ClosedContext(DbContextOptions<ClosedContext> options) : DbContext(options)
{
    public DbContextOptions<ClosedContext> Options { get; } = options;
    public DbSet<ClosedRow> Rows => Set<ClosedRow>();
    public DbSet<ClosedOther> Others => Set<ClosedOther>();
    public int DisposeCount { get; private set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<ClosedView>().HasNoKey().ToView("RowNames");

    public override ValueTask DisposeAsync()
    {
        DisposeCount++;
        return base.DisposeAsync();
    }
}
