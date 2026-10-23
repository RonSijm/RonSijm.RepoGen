using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.Tool.Tests;

internal sealed class ReturningShadowTestHarness(SqliteConnection connection, ReturningShadowContext context) : IAsyncDisposable
{
    internal const string Namespace = "Generated.ReturningShadow";
    internal ReturningShadowContext Context { get; } = context;

    internal static ReturningShadowContext Empty(string shape = "normal", SqliteConnection? connection = null)
    {
        var options = new DbContextOptionsBuilder<ReturningShadowContext>().ReplaceService<IModelCacheKeyFactory, ReturningShadowModelCacheKeyFactory>();
        if (connection is null) options.UseSqlite("Data Source=:memory:"); else options.UseSqlite(connection);
        return new(options.Options, shape);
    }

    internal static async Task<ReturningShadowTestHarness> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var context = Empty(connection: connection); await context.Database.EnsureCreatedAsync();
        foreach (var (tenant, id, scope, credit) in new[] { (1, 1, "alpha", 7), (1, 2, "alpha", 9), (1, 3, "beta", 4), (2, 1, "alpha", 3) })
        {
            var row = new ReturningShadowRow { TenantId = tenant, Id = id, Text = "item" + id, Quantity = 1 };
            context.Rows.Add(row); var entry = context.Entry(row);
            entry.Property("Scope").CurrentValue = scope; entry.Property("Credit").CurrentValue = credit;
            entry.Property("Status").CurrentValue = id == 1 ? ReturningState.Ready : null;
            entry.Property("OptionalNote").CurrentValue = id == 2 ? "original" : null;
            entry.Property("Fingerprint").CurrentValue = id == 1 ? new byte[] { 1, 2, 3 } : null;
        }
        await context.SaveChangesAsync(); context.ChangeTracker.Clear(); return new(connection, context);
    }

    internal static RepositoryModelBuilder Builder(bool include = true, bool leases = false, bool caching = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence")
            .UseReturningWrites<SqliteReturningWriter>().UseAsyncContextLeases(leases));
        if (caching) builder.Configure(options => options.UseClosedCaching());
        var entity = builder.Entity<ReturningShadowRow>(); entity.BindWritePort<IReturningShadowWrites>();
        entity.HasVisibilityPolicy("Tenant", row => row.TenantId == 1);
        entity.HasQuery("Quantities").Cache().OrderBy(row => row.Id).Select(row => row.Quantity).List();
        foreach (var name in new[] { "Adjust", "Remove" })
        {
            var command = entity.HasCommand(name).Where<ReturningShadowRequest>((row, input) => row.Text.StartsWith(input.Prefix));
            if (name == "Adjust") command.Update(setters => setters
                .SetProperty<int, ReturningShadowRequest>(row => row.Quantity, (row, input) => EF.Property<int>(row, "Credit") + input.Delta)
                .SetProperty<string, ReturningShadowRequest>(row => row.Text, (row, input) => row.Text + input.Suffix)
                .SetProperty(row => row.Revision, row => row.Revision + 1)).AllowWrite(row => row.Revision);
            else command.Delete();
            command.ExpectVersion<int, ReturningShadowRequest>(row => row.Revision, (row, input) => input.ExpectedVersion)
                .ReturnsMany<ReturningShadowDto, ReturningShadowRequest>((row, input) => new ReturningShadowDto(row.TenantId, row.Id, row.Text,
                    EF.Property<int>(row, "Quantity"), row.Revision, EF.Property<string>(row, "Normalized"), EF.Property<int>(row, "ComputedVersion"),
                    EF.Property<int>(row, "Credit"), EF.Property<ReturningState?>(row, "Status"), EF.Property<string?>(row, "OptionalNote"),
                    EF.Property<byte[]?>(row, "Fingerprint"), input.Suffix)).IncludeShadowProperties(include);
        }
        entity.HasCommand("Compare").Where<int>((row, expected) => EF.Property<int>(row, "ComputedVersion") == expected)
            .Update(setters => setters.SetProperty(row => row.Quantity, row => row.Quantity + 1)).IncludeShadowProperties(include)
            .ReturnsMany(row => EF.Property<int>(row, "ComputedVersion"));
        entity.HasCommand("Fail").Update(setters => setters.SetProperty(row => row.Quantity, row => row.Quantity + 1)).IncludeShadowProperties(include)
            .ReturnsMany(row => ReturningShadowCalls.Fail(EF.Property<int>(row, "ComputedVersion")));
        entity.HasCommand("ClearNotes").Update(setters => setters.SetProperty(row => row.Text, "cleared")).IncludeShadowProperties(include)
            .ReturnsMany(row => EF.Property<string?>(row, "OptionalNote")).ResultNullability<string>(shape => shape.Nullable());
        return builder;
    }

    internal static Assembly Compile(ReturningShadowContext context, RepositoryModelBuilder? builder = null) =>
        ImmediateTestHarness.CompileProduction(context, builder ?? Builder());
    internal static object Operations(Assembly assembly, object source, object? writer = null, object? cache = null) =>
        cache is null ? Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ReturningShadowRowRepositoryOperations")!, source, writer ?? new SqliteReturningWriter())! :
            Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ReturningShadowRowRepositoryOperations")!, source, cache, writer ?? new SqliteReturningWriter())!;

    public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await connection.DisposeAsync(); }
}
