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

public sealed class MemoryWriteContractTests
{
    [Fact]
    public async Task SameDesignedWriteOutcomesStageSaveAndRollbackRunOnFixturesAndGeneratedSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new MemoryWriteContext(new DbContextOptionsBuilder<MemoryWriteContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new MemoryWriteNote { Id = 1, Title = "old", Version = 1 }, new MemoryWriteNote { Id = 2, Title = "hidden", Version = 1, Hidden = true });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var model = RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: new MemoryWriteRepositoryDesigner().CreateModel());
        var sources = new[] { RepositorySourceRenderer.Render(model), ClosedRepositorySourceRenderer.RenderContracts(model) };
        var references = ScriptedTestDoubleTestHarness.TestingReferences().Append(typeof(MemoryWriteContext).Assembly.Location)
            .Append(typeof(DbContext).Assembly.Location).Append(typeof(RelationalQueryableExtensions).Assembly.Location)
            .Append(typeof(RonSijm.RepoGen.EntityFrameworkCore.TrackedCommandExecutor).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("MemoryWriteProduction_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            references, ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        emitted.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).Should().BeEmpty();
        output.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(output);
        var realNote = Activator.CreateInstance(assembly.GetType("Generated.MemoryWrites.Persistence.MemoryWriteNoteRepositoryOperations")!, context)!;
        var realAudit = Activator.CreateInstance(assembly.GetType("Generated.MemoryWrites.Persistence.MemoryWriteAuditRepositoryOperations")!, context)!;
        var (memoryNote, memoryAudit, data) = Repositories();
        async Task Equal<TResult>(object real, object memory, string name, params object?[] inputs)
        {
            var expected = await Invoke<TResult>(real, name, inputs);
            var actual = await Invoke<TResult>(memory, name, inputs);
            JsonSerializer.Serialize(actual).Should().Be(JsonSerializer.Serialize(expected), name);
        }
        await Equal<CommandResult<MemoryWriteView>>(realNote, memoryNote, "CreateAsync", new MemoryWriteRequest(3, "new"));
        await Equal<CommandResult<MemoryWriteView>>(realNote, memoryNote, "RenameAsync", new MemoryWriteRequest(3, "next"));
        await Equal<CommandResult<MemoryWriteView>>(realNote, memoryNote, "DeleteAsync", new MemoryWriteRequest(3, "", 1));
        await Equal<CommandResult<MemoryWriteView>>(realNote, memoryNote, "RenameAsync", new MemoryWriteRequest(2, "hidden"));
        await Equal<CommandResult<MemoryWriteView>>(realNote, memoryNote, "RenameAsync", new MemoryWriteRequest(3, "unsupplied", 2, false));
        await Equal<CommandResult>(realNote, memoryNote, "StageRenameAsync", new MemoryWriteRequest(1, "staged"));
        await Equal<CommandResult>(realAudit, memoryAudit, "StageAuditAsync", new MemoryWriteRequest(10, "audit"));
        await Equal<MemoryWriteView?>(realNote, memoryNote, "FindAsync", 1);
        (await data.SaveChangesAsync()).Should().Be(await context.SaveChangesAsync());
        await Equal<MemoryWriteView?>(realNote, memoryNote, "FindAsync", 1);
        await Equal<CommandResult<MemoryWriteView>>(realNote, memoryNote, "DeleteAsync", new MemoryWriteRequest(3, "", 2));
        await using (var realTransaction = await context.Database.BeginTransactionAsync())
        await using (await data.BeginTransactionAsync())
        {
            await Equal<CommandResult<MemoryWriteView>>(realNote, memoryNote, "RenameAsync", new MemoryWriteRequest(1, "rollback", 2));
            await Equal<CommandResult>(realAudit, memoryAudit, "StageAuditAsync", new MemoryWriteRequest(11, "unsaved"));
            await realTransaction.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        await Equal<MemoryWriteView?>(realNote, memoryNote, "FindAsync", 1);
        (await context.Set<MemoryWriteAudit>().CountAsync()).Should().Be(AuditStore(data).Count);
    }
}
