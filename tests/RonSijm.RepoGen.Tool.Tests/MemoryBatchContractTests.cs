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
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryBatchContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameNamedBatchesRunAgainstTheGeneratedFixtureAndActualGeneratedSqlite(bool generatedKey)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MemoryWriteContext>().UseSqlite(connection).Options;
        await using var context = generatedKey ? new MemoryGeneratedWriteContext(options) : new MemoryWriteContext(options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new MemoryWriteNote { Id = 1, Title = "one", Version = 1 }, new MemoryWriteNote { Id = 2, Title = "two", Version = 1 }, new MemoryWriteNote { Id = 3, Title = "hidden", Version = 1, Hidden = true });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = Compile(context);
        var realNote = Activator.CreateInstance(assembly.GetType("Generated.MemoryBatches.Persistence.MemoryWriteNoteRepositoryOperations")!, context)!;
        var realAudit = Activator.CreateInstance(assembly.GetType("Generated.MemoryBatches.Persistence.MemoryWriteAuditRepositoryOperations")!, context)!;
        var (memoryNote, memoryAudit, data) = MemoryBatchTestHarness.Repositories();
        async Task Equal<TResult>(object real, object memory, string name, object? input = null)
        {
            var inputs = input is null ? Array.Empty<object?>() : [input];
            var expected = await Invoke<TResult>(real, name, inputs); var actual = await Invoke<TResult>(memory, name, inputs);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected), name);
        }
        await Equal<CommandResult<IReadOnlyList<MemoryWriteView>>>(realNote, memoryNote, "RenameManyAsync", Array.Empty<MemoryWriteRequest>());
        await Equal<CommandResult<IReadOnlyList<MemoryWriteView>>>(realNote, memoryNote, "RenameManyAsync", new MemoryWriteRequest[] { new(1, "no"), new(99, "missing") });
        await Equal<CommandResult<IReadOnlyList<MemoryWriteView>>>(realNote, memoryNote, "RenameManyAsync", new MemoryWriteRequest[] { new(1, "no"), new(3, "hidden") });
        await Equal<CommandResult<IReadOnlyList<MemoryWriteView>>>(realNote, memoryNote, "RenameManyAsync", new MemoryWriteRequest[] { new(1, "no"), new(2, "stale", 0) });
        await Equal<CommandResult<IReadOnlyList<MemoryWriteView>>>(realNote, memoryNote, "UnversionedAsync", new MemoryWriteRequest[] { new(1, "ignored", Supplied: false) });
        await Equal<CommandResult<IReadOnlyList<MemoryWriteView>>>(realNote, memoryNote, "UnversionedAsync", new MemoryWriteRequest[] { new(1, "one") });
        await Equal<CommandResult<IReadOnlyList<MemoryWriteView>>>(realNote, memoryNote, "UnversionedAsync", new MemoryWriteRequest[] { new(2, "ignored", Supplied: false), new(1, "first") });
        await Equal<CommandResult>(realNote, memoryNote, "StageManyAsync", new MemoryWriteRequest[] { new(2, "second"), new(1, "staged") });
        await Equal<IReadOnlyList<MemoryWriteView>>(realNote, memoryNote, "ListAsync");
        await Equal<CommandResult>(realAudit, memoryAudit, "StageAuditAsync", new MemoryWriteRequest(10, "audit"));
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync());
        await Equal<IReadOnlyList<MemoryWriteView>>(realNote, memoryNote, "ListAsync");
        await Equal<CommandResult<IReadOnlyList<MemoryWriteView>>>(realNote, memoryNote, "RenameManyAsync", new MemoryWriteRequest[] { new(2, "B", 2), new(1, "A", 2) });
        await using (var transaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal<CommandResult<IReadOnlyList<MemoryWriteView>>>(realNote, memoryNote, "DeleteManyAsync", new MemoryWriteRequest[] { new(2, "", 3), new(1, "", 3) });
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        await Equal<IReadOnlyList<MemoryWriteView>>(realNote, memoryNote, "ListAsync");
        (await context.Set<MemoryWriteAudit>().CountAsync()).Should().Be(AuditStore(data).Count);
    }

    private static Assembly Compile(MemoryWriteContext context)
    {
        var model = RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: new MemoryBatchRepositoryDesigner().CreateModel());
        var sources = new[] { RepositorySourceRenderer.Render(model), ClosedRepositorySourceRenderer.RenderContracts(model) };
        var references = ScriptedTestDoubleTestHarness.TestingReferences().Append(typeof(MemoryWriteContext).Assembly.Location)
            .Append(typeof(DbContext).Assembly.Location).Append(typeof(RelationalQueryableExtensions).Assembly.Location)
            .Append(typeof(RonSijm.RepoGen.EntityFrameworkCore.TrackedBatchCommandExecutor).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("MemoryBatchProduction_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)), references,
            ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream(); var emitted = compilation.Emit(output);
        emitted.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).Should().BeEmpty();
        output.Position = 0; return AssemblyLoadContext.Default.LoadFromStream(output);
    }
}
