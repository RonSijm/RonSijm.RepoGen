using System.Linq.Expressions;
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

internal static class AtomicFixtureTestHarness
{
    private static readonly Lazy<Assembly> EntityAssembly = new(() => Compile(Builder()));
    private static readonly Lazy<Assembly> MappedAssembly = new(() => Compile(Builder(mapped: true)));
    internal static AtomicFixtureContext Context() => new(new DbContextOptionsBuilder<AtomicFixtureContext>().UseSqlite("Data Source=:memory:").Options);
    internal static RepositoryModelBuilder Builder(bool mapped = false, bool simulate = true, bool strict = true,
        bool keyed = false, bool factories = true, bool overrideAtomic = false, bool suppliedIds = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("Generated.AtomicFixture.Contracts").UseGeneratedNamespace("Generated.AtomicFixture.Persistence")
            .UseAtomicWrites<SqliteAtomicWriter>().UseScriptedTestDoubles("Generated.AtomicFixture.TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestCommands(strict).GenerateOnlyConfiguredEntities());
        if (keyed) builder.Configure(options => options.UseKeyedContextLeases<string>());
        var entity = ImmediateTestHarness.Store(builder, mapped);
        if (factories) entity.HasInMemoryGeneratedValue(row => row.Id, (row, rows) => checked(rows.Select(item => item.Id).DefaultIfEmpty().Max() + 1));
        entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
        var list = entity.HasQuery("List").Where<AtomicFixtureInput>((row, request) => row.OwnerId == request.OwnerId).OrderBy(row => row.Id);
        list.Select(row => new ImmediateView(row.Id, row.Code, row.Amount, row.Previous, row.Version, row.Stamp.ToArray())).List();
        Expression<Func<AtomicFixtureInput, ImmediateRow>> create = request => new ImmediateRow { OwnerId = request.OwnerId, Code = request.Code,
            Amount = request.Amount, Previous = 0, Version = 1, Stamp = request.Stamp ?? new byte[] { 1 }, Hidden = request.Hidden };
        if (suppliedIds) create = request => new ImmediateRow { Id = request.Id, OwnerId = request.OwnerId, Code = request.Code,
            Amount = request.Amount, Previous = 0, Version = 1, Stamp = request.Stamp ?? new byte[] { 1 }, Hidden = request.Hidden };
        var ensure = entity.HasCommand("Ensure").Validate<AtomicFixtureInput>(request => request.Code != "", "A code is required.")
            .Create(create)
            .AllowWrite(row => row.Version).AllowWrite(row => row.Stamp).InsertIfAbsent(row => new { row.OwnerId, row.Code })
            .Returns(row => new ImmediateView(row.Id, row.Code, row.Amount, row.Previous, row.Version, row.Stamp.ToArray()));
        var count = entity.HasCommand("EnsureCount")
            .Create(create)
            .AllowWrite(row => row.Version).AllowWrite(row => row.Stamp).InsertIfAbsent(row => new { row.OwnerId, row.Code });
        var upsert = entity.HasCommand("Upsert").Where<AtomicFixtureInput>((row, request) => row.Amount >= request.Minimum)
            .Create(create)
            .InsertOrUpdate(row => new { row.OwnerId, row.Code }, setters => setters
                .SetProperty<int, AtomicFixtureInput>(row => row.Amount, (row, request) => row.Amount + request.Amount)
                .SetProperty(row => row.Previous, row => row.Amount).SetProperty(row => row.Version, row => row.Version + 1))
            .AllowWrite(row => row.Version).AllowWrite(row => row.Stamp)
            .ExpectVersion<int, AtomicFixtureInput>(row => row.Version, (row, request) => request.ExpectedVersion)
            .ExpectVersion<byte[], AtomicFixtureInput>(row => row.Stamp, (row, request) => request.Stamp ?? new byte[] { 1 })
            .Returns(row => new ImmediateView(row.Id, row.Code, row.Amount, row.Previous, row.Version, row.Stamp.ToArray()));
        var stage = entity.HasCommand("StageCode").Where<AtomicFixtureInput>((row, request) => row.Id == request.Id)
            .Patch(setters => setters.SetProperty<string, AtomicFixtureInput>(row => row.Code, (row, request) => request.Code)).Stage();
        foreach (var command in new[] { ensure, count, upsert })
        {
            if (suppliedIds) command.AllowWrite(row => row.Id);
            command.SimulateImmediateWrites(simulate);
            if (overrideAtomic) command.RequireTestOverride("Provider constraints require an explicit script.");
            if (keyed) command.UseContextKey<AtomicFixtureInput, string>(request => request.Partition);
        }
        if (keyed) { list.UseContextKey<AtomicFixtureInput, string>(request => request.Partition); stage.UseContextKey<AtomicFixtureInput, string>(request => request.Partition); }
        return builder;
    }
    internal static Assembly Compile(RepositoryModelBuilder builder)
    { using var context = Context(); return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build())); }
    internal static (object Fixture, object Repository, InMemoryWriteDataContext Data) Fixture(bool mapped = false, Assembly? assembly = null, string? key = null)
    {
        assembly ??= mapped ? MappedAssembly.Value : EntityAssembly.Value;
        const string ns = "Generated.AtomicFixture.TestDoubles.";
        var fixture = Activator.CreateInstance(assembly.GetType(ns + "AtomicFixtureContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType(ns + "ImmediateRowRepositoryTestDouble")!, fixture)!;
        var data = ImmediateTestHarness.Data(fixture, key);
        if (mapped) ImmediateTestHarness.MappedStore(data).SeedRange(ImmediateTestHarness.Seeds().Select(row =>
            new ImmediateData(row.Id, row.OwnerId, row.Code, row.Amount, row.Previous, row.Hidden, row.Version, row.Stamp.ToArray())));
        else ImmediateTestHarness.EntityStore(data).SeedRange(ImmediateTestHarness.Seeds());
        return (fixture, repository, data);
    }
    internal static Assembly CompileProduction(AtomicFixtureContext context, RepositoryModelBuilder builder)
    {
        var model = RepositoryModelFactory.Create(context, typeof(AtomicFixtureContext).Assembly, design: builder.Build());
        var references = ScriptedTestDoubleTestHarness.TestingReferences().Concat([typeof(AtomicFixtureContext).Assembly.Location, typeof(DbContext).Assembly.Location,
            typeof(RelationalQueryableExtensions).Assembly.Location, typeof(RonSijm.RepoGen.EntityFrameworkCore.AtomicWriteExecutor).Assembly.Location, typeof(SqliteAtomicWriter).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var sources = new[] { RepositorySourceRenderer.Render(model), ClosedRepositorySourceRenderer.RenderContracts(model) };
        var compilation = CSharpCompilation.Create("AtomicFixtureProduction_" + Guid.NewGuid().ToString("N"), sources.Select(source => CSharpSyntaxTree.ParseText(source)), references, ScriptedTestDoubleTestHarness.CompilerOptions());
        using var output = new MemoryStream(); var emitted = compilation.Emit(output);
        emitted.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Select(diagnostic => diagnostic.ToString()).Should().BeEmpty();
        output.Position = 0; return AssemblyLoadContext.Default.LoadFromStream(output);
    }
}
