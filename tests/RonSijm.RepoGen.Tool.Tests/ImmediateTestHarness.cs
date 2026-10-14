using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class ImmediateTestHarness
{
    private static readonly Lazy<Assembly> EntityAssembly = new(() => Compile(Builder()));
    private static readonly Lazy<Assembly> MappedAssembly = new(() => Compile(Builder(mapped: true)));

    internal static ImmediateContext Context() => new(new DbContextOptionsBuilder<ImmediateContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder Builder(bool mapped = false, bool simulate = true, bool strict = true, bool keyed = false,
        bool overrideImmediate = false, bool disableBump = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("Generated.Immediate.Contracts").UseGeneratedNamespace("Generated.Immediate.Persistence")
            .UseReturningWrites<SqliteReturningWriter>().UseAtomicWrites<SqliteAtomicWriter>()
            .UseScriptedTestDoubles("Generated.Immediate.TestDoubles").UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestCommands(strict).GenerateOnlyConfiguredEntities());
        if (keyed) builder.Configure(options => options.UseKeyedContextLeases<string>());
        var entity = Store(builder, mapped);
        entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
        var list = entity.HasQuery("List").Where<ImmediateRequest>((row, request) => row.OwnerId == request.OwnerId).OrderBy(row => row.Id);
        list.Select(row => new ImmediateView(row.Id, row.Code, row.Amount, row.Previous, row.Version, row.Stamp.ToArray())).List();
        var bump = entity.HasCommand("Bump").Validate<ImmediateRequest>(request => request.Delta != 0, "A delta is required.")
            .Where<ImmediateRequest>((row, request) => row.OwnerId == request.OwnerId)
            .Update(setters => setters.SetProperty<int, ImmediateRequest>(row => row.Amount, (row, request) => checked(row.Amount + request.Delta))
                .SetProperty(row => row.Previous, row => row.Amount).SetProperty(row => row.Version, row => row.Version + 1))
            .AllowWrite(row => row.Version).ReturnsMany(row => new ImmediateView(row.Id, row.Code, row.Amount, row.Previous, row.Version, row.Stamp.ToArray()));
        var consume = entity.HasCommand("Consume").Where<ImmediateRequest>((row, request) => row.OwnerId == request.OwnerId && row.Code == request.Code)
            .ExpectVersion<int, ImmediateRequest>(row => row.Version, (row, request) => request.ExpectedVersion).Delete()
            .ReturnsMany(row => new ImmediateView(row.Id, row.Code, row.Amount, row.Previous, row.Version, row.Stamp.ToArray()));
        var update = entity.HasCommand("CountUpdate").Where<ImmediateRequest>((row, request) => row.OwnerId == request.OwnerId)
            .Update(setters => setters.SetProperty(row => row.Previous, row => row.Amount));
        var delete = entity.HasCommand("CountDelete").Where<ImmediateRequest>((row, request) => row.OwnerId == request.OwnerId && row.Code == request.Code).Delete();
        var outcome = entity.HasCommand("CompareAndSet").Where<ImmediateRequest>((row, request) => row.OwnerId == request.OwnerId && row.Code == request.Code)
            .ExpectVersion<int, ImmediateRequest>(row => row.Version, (row, request) => request.ExpectedVersion)
            .Update(setters => setters.SetProperty(row => row.Amount, row => row.Amount + 1)).WithOutcome();
        var stage = entity.HasCommand("StageCode").Where<ImmediateRequest>((row, request) => row.OwnerId == request.OwnerId && row.Id == request.Delta)
            .Patch(setters => setters.SetProperty<string, ImmediateRequest>(row => row.Code, (row, request) => request.Code)).Stage();
        foreach (var command in new[] { bump, consume, update, delete, outcome })
        {
            command.SimulateImmediateWrites(simulate);
            if (ReferenceEquals(command, bump) && disableBump) command.SimulateImmediateWrites(false);
            if (overrideImmediate) command.RequireTestOverride("Native behavior needs a typed script.");
            if (keyed) command.UseContextKey<ImmediateRequest, string>(request => request.Partition);
        }
        if (keyed)
        {
            list.UseContextKey<ImmediateRequest, string>(request => request.Partition);
            stage.UseContextKey<ImmediateRequest, string>(request => request.Partition);
        }
        else
        {
            var stamp = entity.HasCommand("ConsumeStamp").Where<ImmediateStampRequest>((row, request) => row.Id == request.Id)
                .ExpectVersion<byte[], ImmediateStampRequest>(row => row.Stamp, (row, request) => request.Stamp).Delete().SimulateImmediateWrites(simulate)
                .ReturnsMany(row => new ImmediateView(row.Id, row.Code, row.Amount, row.Previous, row.Version, row.Stamp.ToArray()));
            if (overrideImmediate) stamp.RequireTestOverride("Native behavior needs a typed script.");
        }
        return builder;
    }
    internal static RepositoryEntityBuilder<ImmediateRow> Store(RepositoryModelBuilder builder, bool mapped)
    {
        var entity = builder.Entity<ImmediateRow>();
        if (!mapped) entity.HasInMemoryStore(row => row.Id, row => new ImmediateRow
            { Id = row.Id, OwnerId = row.OwnerId, Code = row.Code, Amount = row.Amount, Previous = row.Previous, Hidden = row.Hidden, Version = row.Version, Stamp = row.Stamp.ToArray() });
        else entity.HasInMemoryStore<ImmediateData, int>(row => row.Key, row => new(row.Key, row.OwnerId, row.Code, row.Amount, row.Previous, row.Hidden, row.Revision, row.Marker.ToArray(), row.Tag))
            .MapInMemoryEntity<ImmediateData>(row => new ImmediateRow
                { Id = row.Key, OwnerId = row.OwnerId, Code = row.Code, Amount = row.Amount, Previous = row.Previous, Hidden = row.Hidden, Version = row.Revision, Stamp = row.Marker })
            .MapInMemoryWrites<ImmediateData>((row, saved) => new(row.Id, row.OwnerId, row.Code, row.Amount, row.Previous, row.Hidden, row.Version, row.Stamp.ToArray(), saved == null ? "created" : saved.Tag));
        return entity;
    }
    internal static ImmediateRow[] Seeds() => [new() { Id = 1, OwnerId = 1, Code = "one", Amount = 2, Version = 1, Stamp = [1, 2] },
        new() { Id = 2, OwnerId = 1, Code = "two", Amount = 3, Version = 1, Stamp = [3] },
        new() { Id = 3, OwnerId = 1, Code = "hidden", Amount = 4, Version = 1, Hidden = true, Stamp = [4] },
        new() { Id = 4, OwnerId = 2, Code = "other", Amount = 5, Version = 1, Stamp = [5] }];
    internal static Assembly Compile(RepositoryModelBuilder builder)
    {
        using var context = Context(); return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }
    internal static (object Fixture, object Repository, InMemoryWriteDataContext Data) Fixture(bool mapped = false, Assembly? assembly = null, string? key = null)
    {
        assembly ??= mapped ? MappedAssembly.Value : EntityAssembly.Value;
        const string ns = "Generated.Immediate.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "ImmediateContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType(ns + "ImmediateRowRepositoryTestDouble")!, fixture)!;
        var data = Data(fixture, key);
        if (mapped) MappedStore(data).SeedRange(Seeds().Select(row => new ImmediateData(row.Id, row.OwnerId, row.Code, row.Amount, row.Previous, row.Hidden, row.Version, row.Stamp)));
        else EntityStore(data).SeedRange(Seeds());
        return (fixture, repository, data);
    }
    internal static InMemoryWriteDataContext Data(object fixture, string? key = null) => (InMemoryWriteDataContext)(key is null
        ? fixture.GetType().GetProperty("Data")!.GetValue(fixture)! : fixture.GetType().GetMethod("For")!.Invoke(fixture, [key])!);
    internal static InMemoryStore<ImmediateRow, int> EntityStore(InMemoryWriteDataContext data) =>
        (InMemoryStore<ImmediateRow, int>)data.GetType().GetProperty("ImmediateRowStore")!.GetValue(data)!;
    internal static InMemoryStore<ImmediateData, int> MappedStore(InMemoryWriteDataContext data) =>
        (InMemoryStore<ImmediateData, int>)data.GetType().GetProperty("ImmediateRowStore")!.GetValue(data)!;
    internal static Task<TResult> Invoke<TResult>(object repository, string name, params object?[] inputs) =>
        ProxyWriteTestHarness.Invoke<TResult>(repository, name, inputs);
    internal static Assembly CompileProduction(DbContext context, RepositoryModelBuilder builder)
    {
        var model = RepositoryModelFactory.Create(context, typeof(ImmediateContext).Assembly, design: builder.Build());
        var references = ScriptedTestDoubleTestHarness.TestingReferences().Concat([typeof(ImmediateContext).Assembly.Location, typeof(DbContext).Assembly.Location,
            typeof(RelationalQueryableExtensions).Assembly.Location, typeof(RonSijm.RepoGen.EntityFrameworkCore.ReturningWriteExecutor).Assembly.Location, typeof(SqliteReturningWriter).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var sources = new[] { RepositorySourceRenderer.Render(model), ClosedRepositorySourceRenderer.RenderContracts(model) };
        var compilation = CSharpCompilation.Create("ImmediateProduction_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)), references, ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream(); var emitted = compilation.Emit(output);
        emitted.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
        output.Position = 0; return AssemblyLoadContext.Default.LoadFromStream(output);
    }
}
