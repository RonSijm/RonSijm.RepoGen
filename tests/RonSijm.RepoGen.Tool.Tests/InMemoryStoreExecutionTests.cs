using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryStoreExecutionTests
{
    [Fact]
    public void AllRepositoryFixturesShareScopedStoresWhileOtherScopesAreIsolated()
    {
        using var context = InMemoryStoreGenerationTests.Context();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new MemoryFixtureRepositoryDesigner()));
        var services = new ServiceCollection();
        Register(assembly, services);
        Register(assembly, services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        var fixture = scope.ServiceProvider.GetRequiredService(Type(assembly, "MemoryFixtureContextRepositoryFixture"));
        var row = scope.ServiceProvider.GetRequiredService(Type(assembly, "MemoryFixtureRowRepositoryTestDouble"));
        var label = scope.ServiceProvider.GetRequiredService(Type(assembly, "MemoryFixtureLabelRepositoryTestDouble"));
        var scriptOnly = scope.ServiceProvider.GetRequiredService(Type(assembly, "MemoryFixtureScriptOnlyRepositoryTestDouble"));
        foreach (var repository in new[] { row, label, scriptOnly }) SharedFixture(repository).Should().BeSameAs(fixture);
        foreach (var prefix in new[] { "IRead", "IWrite", "I" })
            scope.ServiceProvider.GetRequiredService(assembly.GetType("Generated.Memory.Contracts." + prefix + "MemoryFixtureRowRepository")!).Should().BeSameAs(row);
        var rows = Rows(fixture);
        rows.Seed(new() { Id = 1, Labels = ["isolated"] });
        var data = (InMemoryDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        data.CaptureBaseline();
        data.Clear();
        data.RestoreBaseline();
        Rows(SharedFixture(label)).Should().BeSameAs(rows);
        Rows(SharedFixture(label)).Find(new(1))!.Labels.Should().Equal("isolated");
        using var other = provider.CreateScope();
        var otherFixture = other.ServiceProvider.GetRequiredService(Type(assembly, "MemoryFixtureContextRepositoryFixture"));
        otherFixture.Should().NotBeSameAs(fixture);
        Rows(otherFixture).Count.Should().Be(0);
    }

    [Fact]
    public void ExplicitSingletonFixtureSharesDataButNotScriptInstancesAcrossRequestScopes()
    {
        using var context = InMemoryStoreGenerationTests.Context();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new MemoryFixtureRepositoryDesigner()));
        var fixtureType = Type(assembly, "MemoryFixtureContextRepositoryFixture");
        var fixture = Activator.CreateInstance(fixtureType)!;
        var services = new ServiceCollection();
        services.AddSingleton(fixtureType, fixture);
        Register(assembly, services);
        services.Count(item => item.ServiceType == fixtureType).Should().Be(1);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var firstRepo = first.ServiceProvider.GetRequiredService(Type(assembly, "MemoryFixtureRowRepositoryTestDouble"));
        var secondRepo = second.ServiceProvider.GetRequiredService(Type(assembly, "MemoryFixtureRowRepositoryTestDouble"));
        firstRepo.Should().NotBeSameAs(secondRepo);
        SharedFixture(firstRepo).Should().BeSameAs(fixture);
        SharedFixture(secondRepo).Should().BeSameAs(fixture);
        Rows(SharedFixture(firstRepo)).Seed(new() { Id = 1 });
        Rows(SharedFixture(secondRepo)).Count.Should().Be(1);
    }

    [Fact]
    public async Task GeneratedTypedKeyPartitionsAndCopyAdaptersWorkWithoutExecutingDesignerQueries()
    {
        using var context = InMemoryStoreGenerationTests.Context();
        const string consumer = """
            using System;
            using System.Threading.Tasks;
            using Generated.Memory.TestDoubles;
            using RonSijm.RepoGen.Tool.Tests;
            using RonSijm.RepoGen.Testing;
            public static class MemoryConsumer
            {
                public static async Task<bool> Exercise()
                {
                    var fixture = new MemoryFixtureContextRepositoryFixture();
                    var firstKey = new MemoryContextKey("one");
                    var secondKey = new MemoryContextKey("two");
                    var first = fixture.For(firstKey);
                    var second = fixture.For(secondKey);
                    var seed = new MemoryFixtureRow { Id = 1, Name = "first", Labels = new() { "label" } };
                    first.MemoryFixtureRowStore.Seed(seed);
                    second.MemoryFixtureRowStore.Seed(new() { Id = 1, Name = "second" });
                    first.MemoryFixtureLabelStore.Seed(new() { Code = "label", Text = "baseline" });
                    first.CaptureBaseline();
                    seed.Labels.Clear();
                    if (first.MemoryFixtureRowStore.Find(new(1))!.Labels.Count != 1) return false;
                    var repository = new MemoryFixtureRowRepositoryTestDouble(fixture);
                    try { await repository.FindAsync(new(firstKey, 1)); return false; }
                    catch (UnarrangedRepositoryOperationException) { }
                    // This is an explicit test handler, not generated query lowering.
                    repository.OnFindAsync.Runs((args, _) =>
                    {
                        var row = repository.Fixture.For(args.Request.Context).MemoryFixtureRowStore.Find(new(args.Request.Id));
                        return Task.FromResult(row is null ? null : new MemoryRowView(row.Id, row.Name));
                    });
                    if ((await repository.FindAsync(new(firstKey, 1)))?.Name != "first") return false;
                    if ((await repository.FindAsync(new(secondKey, 1)))?.Name != "second") return false;
                    first.Clear();
                    first.RestoreBaseline();
                    if (first.MemoryFixtureRowStore.Count != 1 || first.MemoryFixtureLabelStore.Count != 1) return false;
                    if (second.MemoryFixtureRowStore.Find(new(1))!.Name != "second") return false;
                    repository.ResetTestDouble();
                    if (first.MemoryFixtureRowStore.Count != 1) return false;
                    try { await repository.FindAsync(new(firstKey, 1)); return false; }
                    catch (UnarrangedRepositoryOperationException) { }
                    try { await repository.DeleteAsync(new(firstKey, 1)); return false; }
                    catch (UnarrangedRepositoryOperationException) { }
                    return fixture.ContextKeys.Count == 2;
                }
            }
            """;
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new MemoryFixtureRepositoryDesigner(keyed: true)), consumer);
        (await (Task<bool>)assembly.GetType("MemoryConsumer")!.GetMethod("Exercise")!.Invoke(null, null)!).Should().BeTrue();
        assembly.GetReferencedAssemblies().Should().NotContain(name => name.Name!.Contains("EntityFramework", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratedRepositoryRequiresExplicitNonNullSharedFixture()
    {
        using var context = InMemoryStoreGenerationTests.Context();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, new MemoryFixtureRepositoryDesigner()));
        Action create = () => Activator.CreateInstance(Type(assembly, "MemoryFixtureRowRepositoryTestDouble"), [null]);
        create.Should().Throw<TargetInvocationException>().Which.InnerException.Should().BeOfType<ArgumentNullException>();
    }

    private static Type Type(Assembly assembly, string name) => assembly.GetType("Generated.Memory.TestDoubles." + name)!;
    private static object SharedFixture(object repository) => repository.GetType().GetProperty("Fixture")!.GetValue(repository)!;
    private static InMemoryStore<MemoryFixtureRow, MemoryRowKey> Rows(object fixture)
    {
        var data = fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        return (InMemoryStore<MemoryFixtureRow, MemoryRowKey>)data.GetType().GetProperty("MemoryFixtureRowStore")!.GetValue(data)!;
    }
    private static void Register(Assembly assembly, IServiceCollection services) => Type(assembly, "MemoryFixtureContextRepositoryTestDoubleRegistration")
        .GetMethod("AddMemoryFixtureContextRepositoryTestDoubles")!.Invoke(null, [services]);
}
