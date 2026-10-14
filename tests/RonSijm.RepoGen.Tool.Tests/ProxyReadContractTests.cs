using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ProxyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

[Collection("Proxy reads")]
public sealed class ProxyReadContractTests
{
    [Fact]
    public async Task SameNamedOperationsRunAgainstMappedPrivateRowsAndActualGeneratedSqliteRepositories()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ProxyReadContext(new DbContextOptionsBuilder<ProxyReadContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new ProxyReadDepartment { Id = 1, Name = "Office" }, new ProxyReadDepartment { Id = 2, Name = "Hidden", Hidden = true },
            new ProxyReadDepartment { Id = 99, Name = "Hidden", Hidden = true });
        foreach (var row in Rows()) context.Add(new ProxyReadEntity
        {
            Id = row.Key, Name = row.Caption.ToUpperInvariant(), Category = row.Group, Score = row.RawScore * 2,
            Visible = !row.Archived, DepartmentId = row.DepartmentKey, Stamp = row.Stamp
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var model = RepositoryModelFactory.Create(context, typeof(ProxyReadContext).Assembly, design: new ProxyReadRepositoryDesigner().CreateModel());
        var production = Activator.CreateInstance(Compile(model).GetType("Generated.Proxies.Persistence.ProxyReadEntityRepositoryOperations")!, context)!;
        var memory = Repository();
        async Task Equal<TResult>(string name, params object?[] inputs)
        {
            var expected = await Invoke<Task<TResult>>(production, name, inputs);
            var actual = await Invoke<Task<TResult>>(memory, name, inputs);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected), name);
        }
        await Equal<IReadOnlyList<ProxyReadView>>("AllAsync");
        await Equal<ProxyReadView?>("FindAsync", 1);
        await Equal<ProxyReadView?>("FindAsync", 4);
        await Equal<int>("CountAsync");
        await Equal<bool>("AnyAsync", 61);
        await Equal<IReadOnlyList<ProxyReadView>>("SearchAsync", "PA");
        await Equal<IReadOnlyList<ProxyReadView>>("DynamicAsync", "Score", true);
        await Equal<IReadOnlyDictionary<int, string>>("DictionaryAsync");
        await Equal<int>("SumAsync");
        await Equal<IReadOnlyList<ProxyReadTotal>>("ReportAsync");
        await Equal<PagedResult<ProxyReadView>>("PageAsync", 2, 2);
        await Equal<CursorPagedResult<ProxyReadView, int>>("CursorAsync", new CursorPageRequest<int>(2));
        await Equal<IReadOnlyList<ProxyReadView>>("JoinAsync");
        await Equal<IReadOnlyList<ProxyReadView>>("LeftAsync");
        await Equal<IReadOnlyList<int>>("UnmatchedAsync");
        await Equal<IReadOnlyList<string>>("UnionAsync");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    private static Assembly Compile(RepositoryGenerationModel model)
    {
        var sources = new[] { RepositorySourceRenderer.Render(model), ClosedRepositorySourceRenderer.RenderContracts(model) };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(ProxyReadContext).Assembly.Location).Append(typeof(DbContext).Assembly.Location)
            .Append(typeof(RelationalQueryableExtensions).Assembly.Location).Append(typeof(PagedResult<>).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("RepoGenProxyReadProduction", sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            references, ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        result.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
        output.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(output);
    }
}
