using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

internal sealed class BatchCommandTestHarness(BatchContext context, SqliteConnection connection) : IAsyncDisposable
{
    internal BatchContext Context { get; } = context;
    internal SqliteConnection Connection { get; } = connection;
    internal static RepositoryModelBuilder Builder(bool closed = true, bool leases = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        if (closed) builder.Configure(options => options.UseClosedRepositories("Generated.Batches"));
        if (leases) builder.Configure(options => options.UseAsyncContextLeases());
        return builder;
    }

    internal static RepositoryGenerationModel Model(BatchContext context, bool leases = false, int maximum = 10,
        bool versions = true, bool ports = false)
    {
        var builder = Builder(leases: leases);
        var entity = builder.Entity<BatchRow>().ProtectWrite(row => row.Origin);
        entity.ReadEnabled = false;
        if (ports) entity.BindWritePort<IBatchWritePort>();
        foreach (var name in new[] { "Rename", "Stage" })
        {
            var command = entity.HasCommand(name).Where<BatchRequest>((row, request) => row.OwnerId == request.OwnerId)
                .PatchMany<BatchItem, string, BatchRequest>(request => request.Items, row => row.Code, item => item.Code, items =>
                {
                    items.SetPropertyWhen(row => row.Title, (row, item) => item.Title, item => item.Supplied);
                    if (versions) items.SetProperty(row => row.Version, row => row.Version + 1).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion);
                }, maximum).AllowWrite(row => row.Version);
            if (name == "Stage") command.Stage();
            else command.Save().Returns(row => new BatchRowView(row.Code, row.Title, row.Version));
        }

        entity.HasCommand("Delete").Where<BatchRequest>((row, request) => row.OwnerId == request.OwnerId)
            .DeleteMany<BatchItem, string, BatchRequest>(request => request.Items, row => row.Code, item => item.Code,
                items => items.ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion), maximum)
            .Save().Returns(row => new BatchRowView(row.Code, row.Title, row.Version));
        return CreateModel(context, builder);
    }

    internal static RepositoryGenerationModel CreateModel(BatchContext context, RepositoryModelBuilder builder)
        => RepositoryModelFactory.Create(context, typeof(BatchContext).Assembly, design: builder.Build());
    internal static BatchContext CreateContext(SqliteConnection? connection = null, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<BatchContext>();
        if (connection is null) options.UseSqlite("Data Source=:memory:");
        else options.UseSqlite(connection);
        return new(options.AddInterceptors(interceptors).Options);
    }

    internal static object Operations(RepositoryGenerationModel model, params object[] arguments)
        => Activator.CreateInstance(ClosedRepositoryTests.Compile(model, rejectWarnings: true)
            .GetType((model.Settings.GeneratedNamespace ?? typeof(BatchRow).Namespace) + ".BatchRowRepositoryOperations")!, arguments)!;
    internal static async Task<T> Invoke<T>(object operations, string method, BatchRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await (Task<T>)operations.GetType().GetMethod(method + "Async")!.Invoke(operations, [request, cancellationToken])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    internal static async Task<BatchCommandTestHarness> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new BatchOwner { Id = 1, Name = "First" }, new BatchOwner { Id = 2, Name = "Second" });
        context.Rows.AddRange(new BatchRow { Id = 1, Code = "A", Title = "First", OwnerId = 1 },
            new BatchRow { Id = 2, Code = "B", Title = "Second", OwnerId = 1 },
            new BatchRow { Id = 3, Code = "Z", Title = "Other owner", OwnerId = 2 },
            new BatchRow { Id = 4, Code = "H", Title = "Hidden", OwnerId = 1, Hidden = true });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        context.ResetSaves();
        return new(context, connection);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
