using FluentAssertions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ProxyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

[Collection("Proxy reads")]
public sealed class ProxyReadMappingTests
{
    [Fact]
    public void CastingAnInaccessibleEnumDoesNotMakeTheValueExpressionPortable()
    {
        using var context = Context();
        var builder = Builder();
        var entity = Entity(builder);
        entity.MapInMemoryEntity<ProxyReadData>(row => new ProxyReadEntity { Id = row.Key, Score = (int)(row.Archived ? ProxyReadPrivateEnum.First : ProxyReadPrivateEnum.Second) });
        entity.HasQuery("Count").Count();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3203:*public types*");
    }

    [Fact]
    public void AnonymousCarriersMayComposeSourcesButCannotInvokeEntityContainingBehavior()
    {
        using var context = Context();
        var builder = Builder();
        Entity(builder).HasQuery("Behavior").From(sources => sources.Set<ProxyReadEntity>().Select(row => new { row }).Select(pair => pair.ToString())).List();
        var model = RepositoryModelFactory.Create(context, typeof(ProxyReadContext).Assembly, design: builder.Build());
        model.Entities.Single().Queries.Single().InMemoryQuery!.Supported.Should().BeFalse();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void MappingValuesCannotConstructAnotherPublicPersistenceEntity()
    {
        using var context = Context();
        var builder = Builder();
        var entity = Entity(builder);
        entity.MapInMemoryEntity<ProxyReadData>(row => new ProxyReadEntity { Id = new ProxyReadPublicEntity { Id = row.Key }.Id });
        entity.HasQuery("Count").Count();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3203:*persistence entity types*");
    }

    [Fact]
    public void AScalarWithAnObjectMemberNameGeneratesAWarningFreeReadRow()
    {
        using var context = Context();
        var builder = Builder(strict: true);
        var entity = Entity(builder);
        entity.MapInMemoryEntity<ProxyReadData>(row => new ProxyReadEntity { Id = row.Key, GetHashCode = row.RawScore });
        entity.HasQuery("Values").Select(row => row.GetHashCode).List();
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().Contain("public new required global::System.Int32 GetHashCode");
        ScriptedTestDoubleTestHarness.Compile(bundle);
    }

    [Fact]
    public void ReadRowClassNameCannotCollideWithItsMappedScalar()
    {
        using var context = Context();
        var builder = Builder();
        var entity = Entity(builder);
        entity.MapInMemoryEntity<ProxyReadData>(row => new ProxyReadEntity { __RepoGenProxyReadEntityReadRow = row.Key });
        entity.HasQuery("Count").Count();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3203:*reserved read-row declaration*");
    }

    [Fact]
    public void APrivateScalarTypeCannotLeakThroughThePlainReadRow()
    {
        using var context = Context();
        var builder = Builder();
        var entity = Entity(builder);
        entity.MapInMemoryEntity<ProxyReadData>(row => new ProxyReadEntity { Id = row.Key, PrivateCode = new HiddenMemoryKey(row.RawScore) });
        entity.HasQuery("Count").Count();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3203:*PrivateCode*public*type*");
    }

    [Fact]
    public void RegisteredTypeAdaptersAreExpandedInsideScalarMappingAssignments()
    {
        using var context = Context();
        var builder = Builder();
        var adapter = builder.HasTypeAdapter<int, MemoryRowKey>(value => new MemoryRowKey(value), value => value.Value);
        var entity = Entity(builder);
        entity.MapInMemoryEntity<ProxyReadData>(row => new ProxyReadEntity { Id = adapter.ToModel(new MemoryRowKey(row.Key)) });
        entity.HasQuery("Ids").Select(row => row.Id).List();
        var model = RepositoryModelFactory.Create(context, typeof(ProxyReadContext).Assembly, design: builder.Build());
        model.Entities.Single().InMemoryStore!.EntityMapping!.ExpressionSource.Should().NotContain("ToModel").And.Contain("MemoryRowKey");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public async Task NullableMappedValuesAreNotFabricatedAsDefaults()
    {
        using var context = Context();
        var builder = Builder(strict: true);
        var entity = Entity(builder);
        entity.MapInMemoryEntity<ProxyReadData>(row => new ProxyReadEntity
            { Id = row.Key, Optional = row.Caption.Length == 0 ? null : row.Caption, OptionalScore = row.RawScore == 0 ? null : row.RawScore });
        entity.HasQuery("Value").Where<int>((row, id) => row.Id == id).Select(row => row.OptionalScore).SingleOrDefault();
        entity.HasQuery("Text").OrderBy(row => row.Id).Select(row => row.Optional ?? "none").List();
        const string consumer = """
            using System.Threading.Tasks;
            using Generated.Proxies.TestDoubles;
            public static class NullableMappings
            {
                public static async Task<bool> Exercise()
                {
                    var fixture = new ProxyReadContextRepositoryFixture();
                    fixture.Data.ProxyReadEntityStore.Seed(new() { Key = 1 });
                    fixture.Data.ProxyReadEntityStore.Seed(new() { Key = 2, Caption = "yes", RawScore = 7 });
                    var repository = new ProxyReadEntityRepositoryTestDouble(fixture);
                    var text = await repository.TextAsync();
                    return (await repository.ValueAsync(1)) == null && (await repository.ValueAsync(2)) == 7
                        && (await repository.ValueAsync(99)) == null && text[0] == "none" && text[1] == "yes";
                }
            }
            """;
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()), consumer);
        (await (Task<bool>)assembly.GetType("NullableMappings")!.GetMethod("Exercise")!.Invoke(null, null)!).Should().BeTrue();
    }

    [Fact]
    public async Task EntityMappingDoesNotEnableWriteSimulation()
    {
        using var context = Context();
        var builder = Builder(strict: true);
        var entity = Entity(builder);
        entity.WriteEnabled = true;
        entity.HasQuery("Count").Count();
        entity.HasCommand("Delete").Where<int>((row, id) => row.Id == id).Delete();
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle);
        var fixture = Activator.CreateInstance(assembly.GetType("Generated.Proxies.TestDoubles.ProxyReadContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType("Generated.Proxies.TestDoubles.ProxyReadEntityRepositoryTestDouble")!, fixture)!;
        Store(repository).Seed(new() { Key = 1 });
        Func<Task> write = () => Invoke<Task<int>>(repository, "DeleteAsync", 1);
        await write.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        Store(repository).Count.Should().Be(1);
        ScriptedTestDoubleTestHarness.Returns(repository, "OnDeleteAsync", 1);
        (await Invoke<Task<int>>(repository, "DeleteAsync", 1)).Should().Be(1);
        Store(repository).Count.Should().Be(1, "an explicitly scripted outcome does not imply automatic persistence");
    }

    [Theory]
    [InlineData("VisibilityBypass")]
    [InlineData("SortOptOut")]
    public void UnusedUnmappedPoliciesDoNotDisableAnOtherwisePortableQuery(string scenario)
    {
        using var context = Context();
        var builder = Builder(strict: true);
        var entity = Entity(builder);
        var query = entity.HasQuery("Count");
        if (scenario == "VisibilityBypass")
        {
            entity.HasVisibilityPolicy("Private", row => row.Unmapped > 0);
            query.BypassVisibility("Private");
        }
        else
        {
            entity.HasDefaultSort(row => row.Unmapped);
            query.UseDefaultSort(false);
        }
        query.Count();
        var model = RepositoryModelFactory.Create(context, typeof(ProxyReadContext).Assembly, design: builder.Build());
        model.Entities.Single().Queries.Single().InMemoryQuery!.Supported.Should().BeTrue();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void DisabledTestDoublesDoNotInspectUnusedInvalidReadMappings()
    {
        using var context = Context();
        var builder = Builder(strict: true);
        var excluded = Entity(builder);
        excluded.GenerateTestDouble(false).MapInMemoryEntity<ProxyReadData>(row => new ProxyReadEntity());
        var owner = builder.Entity<ProxyReadDepartment>();
        owner.WriteEnabled = false;
        owner.HasInMemoryStore<ProxyReadDepartmentData, int>(row => row.Key, row => new ProxyReadDepartmentData(row.Key, row.Caption, row.Archived))
            .MapInMemoryEntity<ProxyReadDepartmentData>(row => new ProxyReadDepartment { Id = row.Key, Name = row.Caption });
        owner.HasQuery("Excluded").From(sources => sources.Set<ProxyReadEntity>().Select(row => row.Id))
            .RequireTestOverride("The root is explicitly excluded from test doubles.").List();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }
}
