using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScriptedRow
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

public sealed class ScriptedOtherRow
{
    public int Id { get; set; }
}

public sealed class ScriptedContext(DbContextOptions<ScriptedContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScriptedRow>();
        modelBuilder.Entity<ScriptedOtherRow>();
    }
}

public sealed record ScriptedView(int Id, string? Name);
public sealed record ScriptedSearch(IReadOnlyList<int> Ids);
public interface IScriptedReadPort
{
    Task<ScriptedView?> FindAsync(int identifier = 0, CancellationToken token = default);
    Task<ScriptedView?> FindAsync(string text, CancellationToken token = default);
    Task<ScriptedView?> OptionalLookupAsync(string? text = null, CancellationToken token = default);
    Task<IReadOnlyList<ScriptedView>> SearchAsync(ScriptedSearch request, CancellationToken token = default);
    IAsyncEnumerable<ScriptedView> StreamAsync(CancellationToken token = default);
    async Task<bool> ExistsAsync(int id, CancellationToken token = default) => await FindAsync(id, token) is not null;
}

public interface IScriptedWritePort
{
    Task<int> DeleteAsync(int identifier, CancellationToken token = default);
}

public interface IConflictingScriptedPort
{
    void ClearCalls() { }
}

public sealed class ScriptedRowsRepositoryDesigner(bool enabled = true, bool ports = true) : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Generated.Scripted.Contracts")
            .UseGeneratedNamespace("Generated.Scripted.Persistence").GenerateOnlyConfiguredEntities());
        if (enabled) modelBuilder.Configure(options => options.UseScriptedTestDoubles("Generated.Scripted.TestDoubles"));
        modelBuilder.Entity<ScriptedRow>(entity =>
        {
            if (ports) entity.BindReadPort<IScriptedReadPort>().BindWritePort<IScriptedWritePort>();
            entity.HasProjection(row => new ScriptedView(row.Id, row.Name));
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id).ProjectTo<ScriptedView>().SingleOrDefault();
            entity.HasQuery("Find").Where<string>((row, name) => row.Name == name).ProjectTo<ScriptedView>().SingleOrDefault();
            entity.HasQuery("OptionalLookup").Where<string>((row, text) => row.Name == text).AllowNullInput<string>("text")
                .ProjectTo<ScriptedView>().SingleOrDefault();
            entity.HasQuery("Search").Where<ScriptedSearch>((row, request) => request.Ids.Contains(row.Id))
                .OrderBy(row => row.Id).ProjectTo<ScriptedView>().List();
            entity.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<ScriptedView>().Stream();
            entity.HasCommand("Delete").Where<int>((row, id) => row.Id == id).Delete();
        });
    }
}
