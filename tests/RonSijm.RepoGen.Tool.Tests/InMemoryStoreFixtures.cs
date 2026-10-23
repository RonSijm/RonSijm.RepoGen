using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

public readonly record struct MemoryRowKey(int Value);
public readonly record struct MemoryContextKey(string Value);
internal readonly record struct HiddenMemoryKey(int Value);
public sealed record MemoryLookup(MemoryContextKey Context, int Id);
public sealed record MemoryRowView(int Id, string Name);

public sealed class MemoryFixtureRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<string> Labels { get; set; } = [];
}

public sealed class MemoryFixtureLabel
{
    public string Code { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class MemoryFixtureScriptOnly
{
    public int Id { get; set; }
}

internal sealed class HiddenMemoryEntity
{
    public int Id { get; set; }
}

public interface IConflictingMemoryFixturePort
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    void Fixture() { }
}

public interface IGenericConflictingMemoryFixturePort
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    void Fixture<T>() { }
}

public sealed class MemoryFixtureContext(DbContextOptions<MemoryFixtureContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MemoryFixtureRow>().Ignore(row => row.Labels);
        modelBuilder.Entity<MemoryFixtureLabel>().HasKey(row => row.Code);
        modelBuilder.Entity<MemoryFixtureScriptOnly>();
        modelBuilder.Entity<HiddenMemoryEntity>();
    }
}

public sealed class MemoryFixtureRepositoryDesigner(bool stores = true, bool keyed = false, bool declarations = true, bool portable = false) : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.Memory.Contracts")
            .UseGeneratedNamespace("Generated.Memory.Persistence").UseScriptedTestDoubles("Generated.Memory.TestDoubles")
            .UseInMemoryTestStores(stores).UseInMemoryTestQueries(portable).GenerateOnlyConfiguredEntities());
        if (keyed) modelBuilder.Configure(options => options.UseKeyedContextLeases<MemoryContextKey>());
        modelBuilder.Entity<MemoryFixtureRow>(entity =>
        {
            if (declarations) entity.HasInMemoryStore(row => new MemoryRowKey(row.Id),
                row => new MemoryFixtureRow { Id = row.Id, Name = row.Name, Labels = row.Labels.ToList() });
            var query = entity.HasQuery("Find");
            var command = entity.HasCommand("Delete");
            if (keyed)
            {
                query.UseContextKey<MemoryLookup, MemoryContextKey>(request => request.Context)
                    .Where<MemoryLookup>((row, request) => row.Id == request.Id);
                command.UseContextKey<MemoryLookup, MemoryContextKey>(request => request.Context)
                    .Where<MemoryLookup>((row, request) => row.Id == request.Id);
            }
            else
            {
                query.Where<int>((row, id) => row.Id == id);
                command.Where<int>((row, id) => row.Id == id);
            }
            query.Select(row => new MemoryRowView(row.Id, row.Name)).SingleOrDefault();
            command.Delete();
        });
        modelBuilder.Entity<MemoryFixtureLabel>(entity =>
        {
            if (declarations) entity.HasInMemoryStore(row => row.Code, row => new MemoryFixtureLabel { Code = row.Code, Text = row.Text });
            var query = entity.HasQuery("Count");
            if (keyed) query.UseContextKey<MemoryContextKey, MemoryContextKey>(contextKey => contextKey);
            query.Count();
        });
        modelBuilder.Entity<MemoryFixtureScriptOnly>(entity =>
        {
            var query = entity.HasQuery("Count");
            if (keyed) query.UseContextKey<MemoryContextKey, MemoryContextKey>(contextKey => contextKey);
            query.Count();
        });
    }
}
