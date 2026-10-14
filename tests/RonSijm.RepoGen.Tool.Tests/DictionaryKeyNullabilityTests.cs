using System.Reflection;
using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class DictionaryKeyNullabilityTests
{
    [Theory]
    [InlineData("closed")]
    [InlineData("selectors")]
    [InlineData("cqrs")]
    public async Task IndependentNullableKeyPartsAndValuesCompileAndExecute(string surface)
    {
        await using var context = TypeAdapterTests.Context();
        await TypeAdapterTests.Seed(context);
        context.Rows.Add(new AdapterRow { Id = 3, Name = "three", Scope = 7 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = TypeAdapterTests.Builder(closed: surface == "closed", cqrs: surface == "cqrs");
        var entity = builder.Entity<AdapterRow>();
        if (surface == "closed")
        {
            entity.BindReadPort<IAdapterDictionaryPort>();
        }

        entity.HasQuery("ByKey").Select(row => row).Dictionary(row => new AdapterKey<string?>(row.Alias, row.Id), row => row.Alias)
            .KeyNullability<AdapterKey<string>>(type => type.TypeArgument(0, part => part.Nullable()))
            .ResultNullability<string>(type => type.Nullable());
        // Configure the annotations before the result builder, not only after its terminal.
        entity.HasQuery("GroupedKeys").KeyNullability<AdapterKey<string>>(type => type.TypeArgument(0, part => part.Nullable()))
            .Select(row => row).Lookup(row => new AdapterKey<string?>(row.Alias, 0), row => row.Alias)
            .ResultNullability<string>(type => type.Nullable());
        var model = TypeAdapterTests.Model(context, builder);
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        object repository = Activator.CreateInstance(TypeAdapterTests.TypeOf(assembly, surface == "closed" ? "AdapterRowRepositoryOperations" : "AdapterRowRepository"), context)!;
        if (surface == "cqrs")
        {
            repository = Activator.CreateInstance(TypeAdapterTests.TypeOf(assembly, "AdapterRowReadRepository"), repository)!;
        }

        async Task<T> Execute<T>(string name)
        {
            if (surface != "selectors")
            {
                return await TypeAdapterTests.Invoke<Task<T>>(repository, name, CancellationToken.None);
            }

            return await (Task<T>)TypeAdapterTests.TypeOf(assembly, "AdapterRowConfiguredQueryExtensions").GetMethod(name)!.Invoke(null, [repository, CancellationToken.None])!;
        }

        var dictionary = await Execute<IReadOnlyDictionary<AdapterKey<string?>, string?>>("ByKeyAsync");
        dictionary.Should().HaveCount(3);
        dictionary[new AdapterKey<string?>(null, 2)].Should().BeNull();
        dictionary[new AdapterKey<string?>("upper", 1)].Should().Be("upper");
        var lookup = await Execute<IReadOnlyDictionary<AdapterKey<string?>, IReadOnlyList<string?>>>("GroupedKeysAsync");
        lookup.Should().HaveCount(2);
        lookup[new AdapterKey<string?>(null, 0)].Should().Equal(new string?[] { null, null });
        foreach (var query in model.Entities.Single().Queries)
        {
            query.Dictionary!.KeyTypeName.Should().EndWith("<global::System.String?>");
            query.Dictionary.ValueTypeName.Should().Be("global::System.String?");
            query.Dictionary.KeyBodySource.Should().Contain("new global::RonSijm.RepoGen.Tool.Tests.AdapterKey<global::System.String?>");
        }

        if (surface == "closed")
        {
            var read = (IAdapterDictionaryPort)repository;
            (await read.ByKeyAsync()).Should().HaveCount(3);
            var port = assembly.GetType("Adapters.Contracts.IReadAdapterRowRepository")!;
            var method = port.GetInterfaces().Append(port).SelectMany(type => type.GetMethods()).First(method => method.Name == "ByKeyAsync");
            var key = new NullabilityInfoContext().Create(method.ReturnParameter).GenericTypeArguments[0].GenericTypeArguments[0];
            key.ReadState.Should().Be(NullabilityState.NotNull);
            key.GenericTypeArguments[0].ReadState.Should().Be(NullabilityState.Nullable);
        }
    }

    [Fact]
    public void KeyAnnotationsParticipateInSnapshotsWithoutChangingClrOperationIdentity()
    {
        using var context = TypeAdapterTests.Context();
        var before = Snapshot(false);
        var after = Snapshot(true);
        after.CompareTo(before).Should().Contain(change => change.DiagnosticId == "RGA1004" && change.IsBreaking);
        after.Contracts.SelectMany(contract => contract.Methods).Select(method => method.Signature)
            .Should().Equal(before.Contracts.SelectMany(contract => contract.Methods).Select(method => method.Signature));

        RepositoryApiSnapshot Snapshot(bool annotate)
        {
            var builder = TypeAdapterTests.Builder(closed: true);
            var query = builder.Entity<AdapterRow>().HasQuery("Keys").Select(row => row).Dictionary(row => new AdapterKey<string>(row.Name, row.Id));
            // The value must also be EF-free in closed mode.
            query.Dictionary(row => new AdapterKey<string>(row.Name, row.Id), row => row.Name);
            if (annotate)
            {
                query.KeyNullability<AdapterKey<string>>(type => type.TypeArgument(0, part => part.Nullable()));
            }

            return RepositoryGenerator.Generate(context, builder.Build()).CreateApiSnapshot();
        }
    }

    [Theory]
    [InlineData("outer")]
    [InlineData("wrongType")]
    [InlineData("noDictionary")]
    [InlineData("changedTerminal")]
    public void InvalidKeyAnnotationsAreRejectedBeforeRendering(string scenario)
    {
        using var context = TypeAdapterTests.Context();
        var builder = TypeAdapterTests.Builder();
        var entity = builder.Entity<AdapterRow>();
        var query = entity.HasQuery("Bad").Select(row => row);
        if (scenario != "noDictionary")
        {
            query.Dictionary(row => row.Name, row => row.Id);
        }

        if (scenario == "outer")
        {
            query.KeyNullability<string>(type => type.Nullable());
        }
        else if (scenario == "wrongType")
        {
            query.KeyNullability<int>(type => type.Nullable(false));
        }
        else
        {
            query.KeyNullability<string>(type => type.Nullable(false));
        }

        if (scenario == "changedTerminal")
        {
            query.List();
        }

        var action = () => TypeAdapterTests.Model(context, builder);
        action.Should().Throw<InvalidOperationException>().WithMessage("RGN0006:*");
    }

    [Fact]
    public void DuplicateKeyDeclarationsAndStaleBuildersCannotSilentlyOverwriteConfiguration()
    {
        var builder = TypeAdapterTests.Builder();
        var query = builder.Entity<AdapterRow>().HasQuery("Bad");
        query.KeyNullability<string>(type => type.Nullable(false));
        var result = query.Select(row => row);
        var duplicate = () => result.KeyNullability<string>(type => type.Nullable(false));
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("RGN0005:*");
        result.Select(row => row.Name);
        var stale = () => result.KeyNullability<string>(type => type.Nullable(false));
        stale.Should().Throw<InvalidOperationException>().WithMessage("*stale*");
    }
}

public interface IAdapterDictionaryPort
{
    Task<IReadOnlyDictionary<AdapterKey<string?>, string?>> ByKeyAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<AdapterKey<string?>, IReadOnlyList<string?>>> GroupedKeysAsync(CancellationToken cancellationToken = default);
}