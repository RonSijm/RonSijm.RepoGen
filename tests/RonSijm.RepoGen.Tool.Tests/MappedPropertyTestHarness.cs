using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.Tool.Tests;

internal sealed class MappedPropertyTestHarness(SqliteConnection connection, MappedPropertyContext context) : IAsyncDisposable
{
    internal const string Namespace = "Generated.MappedProperties";
    internal MappedPropertyContext Context { get; } = context;
    internal static MappedPropertyContext Empty() => new(new DbContextOptionsBuilder<MappedPropertyContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder Builder(bool closed = true, bool leases = false) => new RepositoryModelBuilder().Configure(options =>
    {
        options.UseSimpleDefaults().GenerateOnlyConfiguredEntities().UseGeneratedNamespace(Namespace + ".Persistence");
        if (closed) options.UseClosedRepositories(Namespace + ".Contracts").UseAsyncContextLeases(leases).UseReturningWrites<SqliteReturningWriter>();
    });
    internal static async Task<MappedPropertyTestHarness> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var context = new MappedPropertyContext(new DbContextOptionsBuilder<MappedPropertyContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        foreach (var id in Enumerable.Range(1, 3))
        {
            var row = new MappedPropertyNote { Id = id, Title = "title" + id }; context.Add(row);
            context.Entry(row).Property("Code").CurrentValue = id == 1 ? "one" : id == 2 ? "two" : "hidden";
            context.Entry(row).Property("Hidden").CurrentValue = id == 3;
            context.Entry(row).Property("State").CurrentValue = id == 1 ? ReturningState.Ready : ReturningState.Pending;
            context.Entry(row).Property("OptionalVersion").CurrentValue = id == 2 ? 7 : null;
            context.Entry(row).Property("BinaryVersion").CurrentValue = id == 1 ? new byte[] { 1, 2 } : null;
        }
        await context.SaveChangesAsync(); context.ChangeTracker.Clear(); return new(connection, context);
    }
    internal static object Operations(MappedPropertyContext context, RepositoryModelBuilder builder, object? source = null)
    {
        var assembly = ImmediateTestHarness.CompileProduction(context, builder);
        var type = assembly.GetType(Namespace + ".Persistence.MappedPropertyNoteRepositoryOperations")!;
        return Activator.CreateInstance(type, type.GetConstructors().Single().GetParameters().Length == 1
            ? [source ?? context] : [source ?? context, new SqliteReturningWriter()])!;
    }
    internal static T ReadSelector<T>(object selector, string method)
    {
        if (selector.GetType().GetMethod(method) is { } instance) return (T)instance.Invoke(selector, [CancellationToken.None])!;
        var extension = selector.GetType().Assembly.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Single(candidate => candidate.Name == method && candidate.GetParameters() is [{ ParameterType: var receiver }, _] && receiver == selector.GetType());
        return (T)extension.Invoke(null, [selector, CancellationToken.None])!;
    }
    internal static RepositoryCommandBuilder<MappedPropertyNote> Versioned(RepositoryModelBuilder builder, string name, bool tracked = false, bool stage = false)
    {
        var command = builder.Entity<MappedPropertyNote>().HasCommand(name).Where<MappedPropertyInput>((row, input) => row.Id == input.Id);
        void Set(RepositoryUpdateBuilder<MappedPropertyNote> updates) => updates.SetProperty<string, MappedPropertyInput>(row => row.Title, (row, input) => input.Title)
            .SetProperty(row => row.Counter, row => row.Counter + 1);
        if (tracked) { command.Patch(Set); if (stage) command.Stage(); else command.Save(); }
        else command.Update(Set);
        return command.ExpectVersion<int, MappedPropertyInput>(MappedPropertyContext.Token, input => input.Version);
    }
    public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await connection.DisposeAsync(); }
}
