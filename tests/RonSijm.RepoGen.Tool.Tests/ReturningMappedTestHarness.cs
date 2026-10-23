using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.SqlServer;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.Tool.Tests;

internal sealed class ReturningMappedTestHarness(SqliteConnection connection, ReturningMappedContext context) : IAsyncDisposable
{
    internal const string Namespace = "Generated.ReturningMapped";
    internal ReturningMappedContext Context { get; } = context;
    internal static ReturningMappedContext Empty(string shape = "normal", SqliteConnection? connection = null, bool sqlServer = false)
    {
        var options = new DbContextOptionsBuilder<ReturningMappedContext>().ReplaceService<IModelCacheKeyFactory, ReturningMappedModelCacheKeyFactory>();
        if (sqlServer) options.UseSqlServer("Server=(local);Database=RepoGenGenerationOnly;Integrated Security=true;TrustServerCertificate=true");
        else if (connection is null) options.UseSqlite("Data Source=:memory:");
        else options.UseSqlite(connection);
        return new(options.Options, shape);
    }

    internal static async Task<ReturningMappedTestHarness> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var context = Empty(connection: connection); await context.Database.EnsureCreatedAsync();
        foreach (var id in new[] { 1, 2, 3 })
        {
            var row = new ReturningMappedRow { Id = id, Title = "item" + id };
            context.Add(row);
            context.Entry(row).Property("state").CurrentValue = id == 1 ? ReturningState.Ready : null;
            context.Entry(row).Property("fingerprint").CurrentValue = id == 1 ? new byte[] { 1, 2, 3 } : null;
            row[ReturningMappedContext.Note] = id == 1 ? "original" : null;
            context.Entry(row).Property("Archived").CurrentValue = id == 3;
        }
        await context.SaveChangesAsync(); context.ChangeTracker.Clear(); return new(connection, context);
    }

    internal static RepositoryModelBuilder Builder(bool mapped = true, bool shadow = false, bool leases = false, bool sqlServer = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence").UseAsyncContextLeases(leases));
        if (sqlServer) builder.Configure(options => options.UseReturningWrites<SqlServerReturningWriter>());
        else builder.Configure(options => options.UseReturningWrites<SqliteReturningWriter>());
        var entity = builder.Entity<ReturningMappedRow>();
        foreach (var name in new[] { "Rename", "Remove" })
        {
            var command = entity.HasCommand(name).Where<ReturningMappedRequest>((row, input) => input.Id == 0 || row.Id == input.Id);
            if (name == "Rename") command.Update(setters => setters.SetProperty<string, ReturningMappedRequest>(row => row.Title, (row, input) => input.Title)
                .SetProperty(row => row.Quantity, row => row.Quantity + 1));
            else command.Delete();
            command.ExpectVersion<int, ReturningMappedRequest>("audit", input => input.Version)
                .ReturnsMany(row => new ReturningMappedView(row.Id, row.Title, row.Caption, EF.Property<int>(row, "audit"),
                    EF.Property<ReturningState?>(row, "state"), EF.Property<byte[]?>(row, "fingerprint"), EF.Property<string?>(row, ReturningMappedContext.Note)))
                .IncludeMappedProperties(mapped).IncludeShadowProperties(shadow);
        }
        entity.HasCommand("Fail").Update(setters => setters.SetProperty(row => row.Title, "failing"))
            .ReturnsMany(row => ReturningMappedCalls.Fail(EF.Property<int>(row, "audit"))).IncludeMappedProperties(mapped).IncludeShadowProperties(shadow);
        return builder;
    }

    internal static Assembly Compile(ReturningMappedContext context, RepositoryModelBuilder? builder = null) => ImmediateTestHarness.CompileProduction(context, builder ?? Builder());
    internal static object Operations(Assembly assembly, object source) => Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ReturningMappedRowRepositoryOperations")!, source, new SqliteReturningWriter())!;
    internal static Task<MutationResult<ReturningMappedView>> Call(object operations, string method, ReturningMappedRequest request, CancellationToken cancellationToken = default) =>
        ReturningWriteTests.Call<Task<MutationResult<ReturningMappedView>>>(operations, method, request, cancellationToken);
    public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await connection.DisposeAsync(); }
}
