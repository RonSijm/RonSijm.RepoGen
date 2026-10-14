using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ApiDataContractTests
{
    [Fact]
    public async Task DesignerOptInCapturesOnlyPublicBoundaryShapesWithoutChangingGeneratedOperations()
    {
        await using var context = Context();
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.Rows.Add(new ClosedRow { Id = 1, Name = "data" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var off = Builder(false);
        var on = Builder(true);
        var ordinary = RepositoryGenerator.Generate(context, off.Build());
        var model = RepositoryModelFactory.Create(context, typeof(ClosedContext).Assembly, design: on.Build());
        var bundle = RepositoryGenerator.Generate(model);
        bundle.Files.Should().Equal(ordinary.Files);
        bundle.Files.Should().NotContain(file => file.Kind == RepositoryOutputKind.ApiSnapshot);
        ordinary.CreateApiSnapshot().SchemaVersion.Should().Be(1);
        ordinary.CreateApiSnapshot().DataContracts.Should().BeNull();
        var snapshot = bundle.CreateApiSnapshot();
        snapshot.SchemaVersion.Should().Be(2);
        snapshot.DataContracts!.Select(contract => contract.Type).Should().BeEquivalentTo(
            Name<ApiSchemaRequest>(), Name<ApiSchemaResponse>(), Name<ApiSchemaDetail>(), Name<ApiSchemaState>());
        snapshot.DataContracts.Should().NotContain(contract => contract.Type.Contains(nameof(ClosedRow), StringComparison.Ordinal) ||
                                                               contract.Type.Contains(nameof(ApiSchemaIntermediate), StringComparison.Ordinal));
        var read = snapshot.Contracts.Single(contract => contract.Type.Contains(".IRead", StringComparison.Ordinal));
        read.Methods.Should().Contain(method => method.ReturnType.Contains(nameof(ApiSchemaResponse), StringComparison.Ordinal));
        var json = snapshot.ToJson();
        RepositoryApiSnapshot.Parse(json).ToJson().Should().Be(json);
        RepositoryGenerator.Generate(context, on.Build(), RepositoryOutputLayout.PerContract).CreateApiSnapshot().ToJson().Should().Be(json);
        bundle.WithApiSnapshot().WithApiSnapshot().Files.Should().ContainSingle(file => file.Kind == RepositoryOutputKind.ApiSnapshot);
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var operations = Activator.CreateInstance(assembly.GetType(typeof(ClosedRow).Namespace + ".ClosedRowRepositoryOperations")!, context)!;
        var result = await (Task<IReadOnlyList<ApiSchemaResponse>>)operations.GetType().GetMethod("ReadAsync")!.Invoke(operations, [new ApiSchemaRequest(1), CancellationToken.None])!;
        result.Should().ContainSingle().Which.Name.Should().Be("data");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public void TraversalHandlesCyclesInheritedMembersGenericDefinitionsAndLargeEnumConstants()
    {
        var data = RepositoryApiDataContractCapture.Capture([typeof(ApiSchemaNode<string>), typeof(ApiSchemaMetadata)]);
        data.Select(contract => contract.Type).Should().OnlyHaveUniqueItems();
        var node = data.Single(contract => contract.Type == Name(typeof(ApiSchemaNode<>)));
        node.GenericParameters.Should().ContainSingle().Which.Name.Should().Be("T");
        node.Members.Single(member => member.Name == "Next").Type.Should().Contain("ApiSchemaNode<global::System.Collections.Generic.List<T?>>?");
        var metadata = data.Single(contract => contract.Type == Name<ApiSchemaMetadata>());
        metadata.Bases.Should().Contain(Name<ApiSchemaParent>());
        metadata.Members.Should().NotContain(member => member.Name == "Inherited" || member.Name == "Hidden" || member.Name == "Static" || member.Name == "Item");
        data.Single(contract => contract.Type == Name<ApiSchemaParent>()).Members.Should().Contain(member => member.Name == "Inherited");
        var required = metadata.Members.Single(member => member.Name == "Required");
        required.Required.Should().BeTrue();
        required.InitOnly.Should().BeTrue();
        metadata.Members.Single(member => member.Name == "ReadOnlyField").Writable.Should().BeFalse();
        metadata.Members.Single(member => member.Name == "WriteOnly").Readable.Should().BeFalse();
        metadata.Members.Single(member => member.Name == "NullSetter").WriteType.Should().Be("global::System.String?");
        metadata.Members.Single(member => member.Name == "Values").Type.Should().Be("global::System.String?[,][]?");
        metadata.Constructors.Should().ContainSingle().Which.SetsRequiredMembers.Should().BeTrue();
        var flags = data.Single(contract => contract.Type == Name<ApiSchemaState>());
        flags.Flags.Should().BeTrue();
        flags.EnumUnderlyingType.Should().Be("global::System.UInt64");
        flags.EnumValues.Single(value => value.Name == "Maximum").Value.Should().Be(ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        data.Single(contract => contract.Type == Name<ApiSchemaValue>()).Constructors.Should().Contain(constructor => constructor.Signature == "()");
    }

    [Fact]
    public void DictionaryKeysCommandInputsAndCursorTypesAreAlsoBoundaryTypes()
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Schema.Contracts").GenerateOnlyConfiguredEntities().IncludeApiDataContracts());
        var entity = builder.Entity<ClosedRow>();
        entity.HasQuery("Keyed").Select(row => new ApiSchemaIntermediate(row.Id, row.Name))
            .Dictionary(row => new ApiSchemaKey(row.Id), row => row.Name);
        entity.HasQuery("After").Select(row => new ApiSchemaResponse { Id = row.Id, Name = row.Name }).OrderBy(row => row.Id)
            .CursorPaged<ApiSchemaCursor>((row, cursor) => row.Id > cursor.Id, row => new ApiSchemaCursor(row.Id));
        entity.HasCommand("Create").Create<ApiSchemaCreate>(request => new ClosedRow { Name = request.Name })
            .Save().Returns(row => new ApiSchemaResponse { Id = row.Id, Name = row.Name });
        var snapshot = RepositoryGenerator.Generate(context, builder.Build()).CreateApiSnapshot();
        snapshot.DataContracts.Should().Contain(contract => contract.Type == Name<ApiSchemaKey>())
            .And.Contain(contract => contract.Type == Name<ApiSchemaCursor>()).And.Contain(contract => contract.Type == Name<ApiSchemaCreate>())
            .And.NotContain(contract => contract.Type == Name<ApiSchemaIntermediate>());
    }

    [Fact]
    public void ExternalDefaultPortMethodsContributeTheirPublicDataTypes()
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Schema.Contracts").GenerateOnlyConfiguredEntities().IncludeApiDataContracts());
        var entity = builder.Entity<ClosedRow>();
        entity.WriteEnabled = false;
        entity.BindReadPort<IApiSchemaPort>();
        entity.HasQuery("Count").Count();
        RepositoryGenerator.Generate(context, builder.Build()).CreateApiSnapshot().DataContracts.Should().Contain(contract => contract.Type == Name<ApiSchemaRequest>());
    }

    [Fact]
    public void PreviouslyEnabledSchemaChecksCannotBeSilentlyDisabledAndOldBaselinesRemainReadable()
    {
        using var context = Context();
        var old = RepositoryGenerator.Generate(context, Builder(false).Build()).CreateApiSnapshot();
        var current = RepositoryGenerator.Generate(context, Builder(true).Build()).CreateApiSnapshot();
        old.ToJson().Should().NotContain("dataContracts");
        RepositoryApiSnapshot.Parse(old.ToJson()).ToJson().Should().Be(old.ToJson());
        current.CompareTo(old).Should().ContainSingle().Which.DiagnosticId.Should().Be("RGA0010");
        current.CompareTo(old).Should().NotContain(change => change.IsBreaking);
        old.CompareTo(current, RepositoryApiCompatibilityMode.Consumers).Should().ContainSingle().Which.DiagnosticId.Should().Be("RGA0004");
        current.CompareTo(current).Should().BeEmpty();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.IncludeApiDataContracts());
        Action unsupported = () => RepositoryGenerator.Generate(context, builder.Build());
        unsupported.Should().Throw<InvalidOperationException>().WithMessage("*RGA0003*closed*");
        builder = Builder(true);
        builder.Configure(options => options.IncludeApiDataContracts(false));
        RepositoryGenerator.Generate(context, builder.Build()).CreateApiSnapshot().ToJson().Should().Be(old.ToJson());
    }

    [Theory]
    [InlineData("public string? Name { get; init; }", "public int Name { get; init; }", "RGA1104")]
    [InlineData("public string? Name { get; init; }", "public string Name { get; init; } = \"\";", "RGA1104")]
    [InlineData("public int Id { get; set; }", "public int Id { get; init; }", "RGA1105")]
    [InlineData("public int Id { get; set; }", "public int Id { get; private set; }", "RGA1105")]
    [InlineData("public int Id { get; set; }", "public int Other { get; set; }", "RGA1103")]
    [InlineData("public string Name { get; init; } = \"\";", "public required string Name { get; init; }", "RGA1106")]
    [InlineData("", "public required string Name { get; init; }", "RGA1106")]
    [InlineData("public int Id;", "public readonly int Id;", "RGA1105")]
    public void ReflectionDetectsActualDataMemberChanges(string before, string after, string diagnostic)
    {
        var baseline = Source("public class Payload { " + before + " }");
        var candidate = Source("public class Payload { " + after + " }");
        candidate.CompareTo(baseline).Should().Contain(change => change.DiagnosticId == diagnostic && change.IsBreaking);
    }

    [Theory]
    [InlineData("public class Payload { public Payload(int id) {} }", "public class Payload { public Payload(string id) {} }", "RGA1107", true)]
    [InlineData("public class Payload { public Payload(string name) {} }", "public class Payload { public Payload(string? name) {} }", "RGA1108", true)]
    [InlineData("public class Payload { public Payload(int id = 7) {} }", "public class Payload { public Payload(int id) {} }", "RGA1108", true)]
    [InlineData("public class Payload { public Payload(int id) {} }", "public class Payload { public Payload(int identifier) {} }", "RGA1108", true)]
    [InlineData("public class Payload { public Payload(int id = 7) {} }", "public class Payload { public Payload(int id = 8) {} }", "RGA1114", false)]
    [InlineData("public enum Payload : ulong { A = 1, B = ulong.MaxValue }", "public enum Payload : ulong { A = 2, B = ulong.MaxValue }", "RGA1109", true)]
    [InlineData("public enum Payload { A, B }", "public enum Payload { A }", "RGA1109", true)]
    [InlineData("public enum Payload { A }", "public enum Payload { A, B }", "RGA1112", false)]
    [InlineData("public class Payload {}", "public abstract class Payload {}", "RGA1102", true)]
    [InlineData("public class Payload {}", "public sealed class Payload {}", "RGA1102", true)]
    [InlineData("public class Payload {}", "public struct Payload {}", "RGA1102", true)]
    [InlineData("public class Payload {}", "public class Payload { public int Id { get; init; } }", "RGA1113", false)]
    [InlineData("public class Payload<T> {}", "public class Payload<T> where T : class, new() {}", "RGA1102", true)]
    [InlineData("public class Payload<T> {}", "public class Payload<T> where T : notnull {}", "RGA1102", true)]
    [InlineData("public class Payload<T> where T : class? {}", "public class Payload<T> where T : class {}", "RGA1102", true)]
    public void ConstructionRepresentationAndAdvisoriesAreCompared(string before, string after, string diagnostic, bool breaking)
    {
        var changes = Source(after).CompareTo(Source(before));
        changes.Should().Contain(change => change.DiagnosticId == diagnostic && change.IsBreaking == breaking);
    }

    [Fact]
    public void DataInterfacesRespectConsumerVersusImplementerPolicyAndRemovedTypesFail()
    {
        var baseline = Source("public interface Payload { int Id { get; } }");
        var candidate = Source("public interface Payload { int Id { get; } string Name { get; } }");
        candidate.CompareTo(baseline).Should().ContainSingle().Which.DiagnosticId.Should().Be("RGA1110");
        candidate.CompareTo(baseline, RepositoryApiCompatibilityMode.Consumers).Should().ContainSingle().Which.IsBreaking.Should().BeFalse();
        new RepositoryApiSnapshot(candidate.Context, [], []).CompareTo(candidate).Should().ContainSingle().Which.DiagnosticId.Should().Be("RGA1101");
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"generatedBy\":\"RonSijm.RepoGen\",\"context\":\"C\",\"contracts\":[]}")]
    [InlineData("{\"schemaVersion\":2,\"generatedBy\":\"RonSijm.RepoGen\",\"context\":\"C\",\"contracts\":[],\"dataContracts\":null}")]
    [InlineData("{\"schemaVersion\":1,\"generatedBy\":\"RonSijm.RepoGen\",\"context\":\"C\",\"contracts\":[],\"dataContracts\":[]}")]
    [InlineData("{\"schemaVersion\":2,\"generatedBy\":\"RonSijm.RepoGen\",\"context\":\"C\",\"contracts\":[],\"dataContracts\":[{}]}")]
    public void InvalidOrDowngradedSchemaMetadataIsRejected(string json)
    {
        Action parse = () => RepositoryApiSnapshot.Parse(json);
        parse.Should().Throw<ArgumentException>().WithMessage("*RGA0001*");
    }

    [Fact]
    public void SchemaCollectionsAreDeepFrozenAndMetadataConflictsAreRejected()
    {
        var data = RepositoryApiDataContractCapture.Capture([typeof(ApiSchemaResponse), typeof(ApiSchemaRequest)]).ToArray();
        var original = data.Single(contract => contract.Type == Name<ApiSchemaResponse>());
        var members = original.Members.ToArray();
        var request = data.Single(contract => contract.Type == Name<ApiSchemaRequest>());
        var parameters = request.Constructors[0].Parameters.ToArray();
        data[Array.IndexOf(data, original)] = original with { Members = members };
        data[Array.IndexOf(data, request)] = request with { Constructors = [request.Constructors[0] with { Parameters = parameters }] };
        var snapshot = new RepositoryApiSnapshot("C", [], data);
        var json = snapshot.ToJson();
        members[0] = members[0] with { Type = "changed" };
        parameters[0] = parameters[0] with { Name = "changed" };
        data[0] = data[0] with { Type = "changed" };
        snapshot.ToJson().Should().Be(json);
        Action duplicate = () => _ = new RepositoryApiSnapshot("C", [], [original, original]);
        duplicate.Should().Throw<ArgumentException>().WithMessage("*RGA0005*duplicate*");
        Action invalid = () => _ = new RepositoryApiSnapshot("C", [], [original with { Members = [original.Members[0] with { Writable = true, WriteType = null }] }]);
        invalid.Should().Throw<ArgumentException>().WithMessage("*RGA0005*member*");
    }

    [Fact]
    public void CliGatesDtoOnlyBreaksBeforeWritesAndChecksSchemaCandidatesWithoutAcceptingBaselines()
    {
        using var context = Context();
        var bundle = RepositoryGenerator.Generate(context, new ApiDataFixtureDesigner()).WithApiSnapshot();
        var snapshot = bundle.CreateApiSnapshot();
        var response = snapshot.DataContracts!.Single(contract => contract.Type == Name<ApiSchemaResponse>());
        var baseline = new RepositoryApiSnapshot(snapshot.Context, snapshot.Contracts, snapshot.DataContracts!.Select(contract => contract == response
            ? contract with
            {
                Members = contract.Members.Select(member => member.Name == "Name" ? member with { Type = "global::System.Int32", WriteType = "global::System.Int32" } : member).ToArray()
            }
            : contract).ToArray());
        snapshot.CompareTo(baseline).Should().ContainSingle().Which.DiagnosticId.Should().Be("RGA1104");
        var root = Path.Combine(Path.GetTempPath(), "repogen-data-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var baselinePath = Path.Combine(root, "baseline.json");
        var generated = Path.Combine(root, "generated");
        var locations = new RepositoryOutputLocations(Path.Combine(generated, "repos.g.cs"), Path.Combine(generated, "contracts.g.cs"), ApiSnapshot: Path.Combine(generated, "candidate.json"));
        var project = Path.GetFullPath("../../../RonSijm.RepoGen.Tool.Tests.csproj", AppContext.BaseDirectory);
        string[] args =
        [
            "--project", project, "--designer-project", project, "--designer", nameof(ApiDataFixtureDesigner), "--context", nameof(ClosedContext),
            "--configuration", "Release", "--no-build", "--output", locations.Repositories, "--contracts-output", locations.Contracts!, "--api-output", locations.ApiSnapshot!, "--api-baseline", baselinePath
        ];
        try
        {
            File.WriteAllText(baselinePath, baseline.ToJson());
            RepositoryGeneratorApplication.Run(args).Should().Be(4);
            Directory.Exists(generated).Should().BeFalse();
            File.ReadAllText(baselinePath).Should().Be(baseline.ToJson());
            File.WriteAllText(baselinePath, snapshot.ToJson());
            RepositoryGeneratorApplication.Run([.. args, "--validate"]).Should().Be(0);
            Directory.Exists(generated).Should().BeFalse();
            RepositoryGeneratorApplication.Run(args).Should().Be(0);
            bundle.Check(locations).Should().BeEmpty();
            RepositoryGeneratorApplication.Run([.. args, "--check"]).Should().Be(0);
            File.ReadAllText(baselinePath).Should().Be(snapshot.ToJson());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static RepositoryModelBuilder Builder(bool include)
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Schema.Contracts")
            .GenerateOnlyConfiguredEntities().IncludeApiDataContracts(include));
        var entity = builder.Entity<ClosedRow>();
        entity.WriteEnabled = false;
        entity.HasQuery("Read").Where<ApiSchemaRequest>((row, request) => row.Id >= request.Minimum)
            .Select(row => new ApiSchemaIntermediate(row.Id, row.Name))
            .Select(row => new ApiSchemaResponse { Id = row.Id, Name = row.Name, State = ApiSchemaState.Ready }).List();
        return builder;
    }

    private static ClosedContext Context() => new(new DbContextOptionsBuilder<ClosedContext>().UseSqlite("Data Source=:memory:").Options);
    private static string Name<T>() => Name(typeof(T));
    private static string Name(Type type) => "global::" + type.FullName!.Replace('+', '.');

    private static RepositoryApiSnapshot Source(string declarations)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ApiData" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText("#nullable enable\nnamespace SnapshotFixtures { " + declarations + " }")], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        emitted.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var assembly = Assembly.Load(output.ToArray());
        return new RepositoryApiSnapshot("C", [], RepositoryApiDataContractCapture.Capture(assembly.GetTypes().Where(static type => type.Name.StartsWith("Payload", StringComparison.Ordinal))));
    }
}

public sealed record ApiSchemaRequest(int Minimum);

public sealed record ApiSchemaCreate(string Name);

public sealed record ApiSchemaIntermediate(int Id, string Name);

public sealed record ApiSchemaKey(int Id);

public sealed record ApiSchemaCursor(int Id);

public sealed record ApiSchemaDetail(string? Text);

public sealed record ApiSchemaResponse
{
    public int Id { get; init; }
    public string? Name { get; init; }
    public ApiSchemaState State { get; init; }
    public ApiSchemaDetail? Detail { get; init; }
}

[Flags]
public enum ApiSchemaState : ulong
{
    Ready = 1,
    Maximum = ulong.MaxValue
}

public sealed class ApiSchemaNode<T>
{
    public ApiSchemaNode<List<T>>? Next { get; set; }
}

public class ApiSchemaParent
{
    public int Inherited { get; set; }
}

public readonly record struct ApiSchemaValue(int Id);

public sealed class ApiSchemaMetadata : ApiSchemaParent
{
    public required string Required { get; init; }
#pragma warning disable CA1051 // Tests snapshot public fields as well as properties.
    public readonly int ReadOnlyField;
#pragma warning restore CA1051
    public string WriteOnly { private get; set; } = "";

    [AllowNull]
    public string NullSetter { get; set; } = "";

    public string?[,][]? Values { get; set; }
    public ApiSchemaValue Value { get; set; }
    public ApiSchemaState State { get; set; }
    private string Hidden { get; set; } = "";
    public static string Static { get; set; } = "";
    public string this[int index] => index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [SetsRequiredMembers]
    public ApiSchemaMetadata()
    {
        Required = "";
    }
}

public interface IApiSchemaPort
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    ApiSchemaRequest NewRequest() => new(1);
}

public sealed class ApiDataFixtureDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Schema.Contracts").GenerateOnlyConfiguredEntities().IncludeApiDataContracts());
        var entity = modelBuilder.Entity<ClosedRow>();
        entity.WriteEnabled = false;
        entity.HasQuery("Read").Select(row => new ApiSchemaResponse { Id = row.Id, Name = row.Name }).List();
    }
}