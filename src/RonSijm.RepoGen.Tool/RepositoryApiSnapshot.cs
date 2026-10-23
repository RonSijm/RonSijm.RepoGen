using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

public enum RepositoryApiCompatibilityMode
{
    ConsumersAndImplementers,
    Consumers
}

public enum RepositoryApiChangeImpact
{
    Breaking,
    Advisory
}

public sealed record RepositoryApiParameter(string Name, string Type, bool Optional = false, string? DefaultValue = null);

public sealed record RepositoryApiMethod(string Signature, string ReturnType, IReadOnlyList<RepositoryApiParameter> Parameters, bool RequiresImplementation = true);

public sealed record RepositoryApiContract(string Type, IReadOnlyList<string> Bases, IReadOnlyList<RepositoryApiMethod> Methods);

public sealed record RepositoryApiDifference(string DiagnosticId, string Contract, string? Member, string Message, RepositoryApiChangeImpact Impact)
{
    [JsonIgnore]
    public bool IsBreaking => Impact == RepositoryApiChangeImpact.Breaking;
}

/// <summary>A deterministic source-contract baseline for generated closed interfaces, including configured bound-port operations.</summary>
/// <remarks>Optional data shapes cover public members/construction, not serialization, method bodies, implementation/SQL behavior or full CLR binary compatibility.</remarks>
public sealed class RepositoryApiSnapshot
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public string GeneratedBy { get; } = "RonSijm.RepoGen";
    public int SchemaVersion => DataContracts is null ? 1 : 2;
    public string Context { get; }
    public IReadOnlyList<RepositoryApiContract> Contracts { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RepositoryApiDataContract>? DataContracts { get; }

    [JsonConstructor]
    public RepositoryApiSnapshot(string context, IReadOnlyList<RepositoryApiContract> contracts, IReadOnlyList<RepositoryApiDataContract>? dataContracts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        ArgumentNullException.ThrowIfNull(contracts);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var frozen = new List<RepositoryApiContract>();
        foreach (var contract in contracts)
        {
            if (contract is null || string.IsNullOrWhiteSpace(contract.Type) || !names.Add(contract.Type) || contract.Bases is null || contract.Methods is null ||
                contract.Bases.Any(string.IsNullOrWhiteSpace) || contract.Bases.Distinct(StringComparer.Ordinal).Count() != contract.Bases.Count)
            {
                throw new ArgumentException("RGA0001: Invalid or duplicate API contract.", nameof(contracts));
            }

            var members = new HashSet<string>(StringComparer.Ordinal);
            var methods = new List<RepositoryApiMethod>();
            foreach (var method in contract.Methods)
            {
                if (method is null || string.IsNullOrWhiteSpace(method.Signature) || string.IsNullOrWhiteSpace(method.ReturnType) ||
                    !members.Add(method.Signature) || method.Parameters is null || method.Parameters.Any(parameter => parameter is null ||
                                                                                                                      string.IsNullOrWhiteSpace(parameter.Name) || string.IsNullOrWhiteSpace(parameter.Type)))
                {
                    throw new ArgumentException("RGA0001: Invalid or duplicate API member.", nameof(contracts));
                }

                methods.Add(method with { Parameters = Array.AsReadOnly(method.Parameters.ToArray()) });
            }

            frozen.Add(contract with
            {
                Bases = Array.AsReadOnly(contract.Bases.Order(StringComparer.Ordinal).ToArray()),
                Methods = Array.AsReadOnly(methods.OrderBy(static method => method.Signature, StringComparer.Ordinal).ToArray())
            });
        }

        Context = context;
        Contracts = Array.AsReadOnly(frozen.OrderBy(static contract => contract.Type, StringComparer.Ordinal).ToArray());
        DataContracts = dataContracts is null ? null : RepositoryApiDataContractCapture.Freeze(dataContracts);
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions) + Environment.NewLine;

    public static RepositoryApiSnapshot Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() is not (1 or 2) ||
                !document.RootElement.TryGetProperty("generatedBy", out var generator) || generator.GetString() != "RonSijm.RepoGen")
            {
                throw new ArgumentException("RGA0001: Unsupported API baseline format.", nameof(json));
            }

            var hasData = document.RootElement.TryGetProperty("dataContracts", out var data) && data.ValueKind == JsonValueKind.Array;
            if (schema.GetInt32() == 2 != hasData || schema.GetInt32() == 1 && document.RootElement.TryGetProperty("dataContracts", out _))
            {
                throw new ArgumentException("RGA0001: API baseline version does not match its data-contract inventory.", nameof(json));
            }

            return JsonSerializer.Deserialize<RepositoryApiSnapshot>(json, JsonOptions) ?? throw new ArgumentException("RGA0001: Empty API baseline.", nameof(json));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        {
            throw new ArgumentException("RGA0001: Invalid API baseline JSON.", nameof(json), exception);
        }
    }

    /// <summary>Compare this candidate with an explicitly approved baseline; never writes or accepts a baseline.</summary>
    public IReadOnlyList<RepositoryApiDifference> CompareTo(RepositoryApiSnapshot baseline,
        RepositoryApiCompatibilityMode mode = RepositoryApiCompatibilityMode.ConsumersAndImplementers)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var differences = new List<RepositoryApiDifference>();

        void Add(string id, string contract, string? member, string message, RepositoryApiChangeImpact impact = RepositoryApiChangeImpact.Breaking)
            => differences.Add(new(id, contract, member, message, impact));

        if (Context != baseline.Context)
        {
            Add("RGA0002", Context, null, "The baseline belongs to a different context; do not compare unrelated APIs.");
        }

        var current = Contracts.ToDictionary(static contract => contract.Type, StringComparer.Ordinal);
        foreach (var old in baseline.Contracts)
        {
            if (!current.TryGetValue(old.Type, out var contract))
            {
                Add("RGA1001", old.Type, null, "Public repository contract removed or renamed.");
                continue;
            }

            foreach (var parent in old.Bases.Except(contract.Bases, StringComparer.Ordinal))
            {
                Add("RGA1002", old.Type, null, "Base contract removed: " + parent);
            }

            var methods = contract.Methods.ToDictionary(static method => method.Signature, StringComparer.Ordinal);
            foreach (var prior in old.Methods)
            {
                if (!methods.TryGetValue(prior.Signature, out var method))
                {
                    Add("RGA1003", old.Type, prior.Signature, "Operation removed, renamed or changed input CLR types.");
                    continue;
                }

                if (prior.ReturnType != method.ReturnType)
                {
                    Add("RGA1004", old.Type, prior.Signature, "Return type or its nullable annotations changed.");
                }

                if (prior.Parameters.Count != method.Parameters.Count)
                {
                    Add("RGA1003", old.Type, prior.Signature, "Operation parameters changed.");
                    continue;
                }

                for (var index = 0; index < prior.Parameters.Count; index++)
                {
                    var before = prior.Parameters[index];
                    var after = method.Parameters[index];
                    if (before.Name != after.Name)
                    {
                        Add("RGA1005", old.Type, prior.Signature, $"Parameter '{before.Name}' renamed to '{after.Name}'; named arguments can break.");
                    }

                    if (before.Type != after.Type)
                    {
                        Add("RGA1004", old.Type, prior.Signature, $"Parameter '{before.Name}' type or nullable annotations changed.");
                    }

                    if (before.Optional && !after.Optional)
                    {
                        Add("RGA1006", old.Type, prior.Signature, $"Parameter '{before.Name}' is no longer optional.");
                    }
                    else if (before.Optional && after.Optional && before.DefaultValue != after.DefaultValue)
                    {
                        Add("RGA1008", old.Type, prior.Signature, $"Parameter '{before.Name}' default changed; recompiled callers can behave differently.", RepositoryApiChangeImpact.Advisory);
                    }
                }

                if (!prior.RequiresImplementation && method.RequiresImplementation && mode == RepositoryApiCompatibilityMode.ConsumersAndImplementers)
                {
                    Add("RGA1007", old.Type, prior.Signature, "A default implementation is now required from interface implementers.");
                }
            }

            foreach (var method in contract.Methods.Where(method => old.Methods.All(prior => prior.Signature != method.Signature)))
            {
                if (method.RequiresImplementation && mode == RepositoryApiCompatibilityMode.ConsumersAndImplementers)
                {
                    Add("RGA1007", old.Type, method.Signature, "New required operation breaks handwritten implementers/mocks; consumer-only mode allows it.");
                }
                else if (old.Methods.Any(prior => MethodName(prior.Signature) == MethodName(method.Signature)))
                {
                    Add("RGA1009", old.Type, method.Signature, "New overload: review null/default-literal overload resolution at call sites.", RepositoryApiChangeImpact.Advisory);
                }
            }

            if (mode == RepositoryApiCompatibilityMode.ConsumersAndImplementers)
            {
                foreach (var parent in contract.Bases.Except(old.Bases, StringComparer.Ordinal))
                {
                    Add("RGA1010", old.Type, null, "New base contract may add obligations for handwritten implementers: " + parent);
                }
            }
        }

        differences.AddRange(RepositoryApiDataContractComparison.Compare(DataContracts, baseline.DataContracts, mode, Context));
        return Array.AsReadOnly(differences.OrderBy(static item => item.Contract, StringComparer.Ordinal)
            .ThenBy(static item => item.Member, StringComparer.Ordinal).ThenBy(static item => item.DiagnosticId, StringComparer.Ordinal)
            .ThenBy(static item => item.Message, StringComparer.Ordinal).ToArray());
    }

    private static string MethodName(string signature) => signature.Split('(')[0];

    internal static RepositoryApiSnapshot Capture(RepositoryGenerationModel model)
    {
        var ns = model.Settings.ContractNamespace ?? throw new InvalidOperationException("RGA0003: API snapshots require closed repositories, not the legacy selector surface.");
        var contracts = new List<RepositoryApiContract>();
        foreach (var entity in model.Entities)
        {
            var prefix = "global::" + ns + ".";
            var read = prefix + "IRead" + entity.EntityName + "Repository";
            var write = prefix + "IWrite" + entity.EntityName + "Repository";
            if (entity.ReadEnabled)
            {
                contracts.Add(new(read, Bases(entity.ReadPorts),
                    Merge(entity.Queries.Where(static query => query.Target == RepositoryQueryTarget.Read).Select(Query), entity.ReadPorts)));
            }

            if (entity.WriteEnabled)
            {
                contracts.Add(new(write, Bases(entity.WritePorts),
                    Merge(entity.Queries.Where(static query => query.Target == RepositoryQueryTarget.Write).Select(Query).Concat(entity.Commands.Select(Command)), entity.WritePorts)));
            }

            if (entity.ReadEnabled || entity.WriteEnabled)
            {
                contracts.Add(new(prefix + "I" + entity.EntityName + "Repository",
                    new[] { entity.ReadEnabled ? read : null, entity.WriteEnabled ? write : null }.OfType<string>().ToArray(), []));
            }
        }

        return new(model.ContextTypeName, contracts, model.DataContracts);
    }

    private static string[] Bases(IReadOnlyList<BoundPortModel>? ports) => (ports ?? [])
        .SelectMany(static port => port.InheritedTypes ?? [port.TypeName]).Distinct(StringComparer.Ordinal).ToArray();

    private static RepositoryApiMethod[] Merge(IEnumerable<RepositoryApiMethod> configured, IReadOnlyList<BoundPortModel>? ports)
    {
        var methods = configured.ToDictionary(static method => method.Signature, StringComparer.Ordinal);
        foreach (var method in (ports ?? []).SelectMany(static port => port.Methods ?? []))
        {
            if (!method.RequiresImplementation && methods.ContainsKey(method.Signature))
            {
                continue; // A generated declaration still requires implementation, even over an inherited default.
            }

            if (methods.TryGetValue(method.Signature, out var existing) && existing.ReturnType != method.ReturnType)
            {
                throw new InvalidOperationException($"RGA0001: Conflicting inherited API operation: {method.Signature}: '{existing.ReturnType}' versus '{method.ReturnType}'.");
            }

            methods[method.Signature] = method;
        }

        return methods.Values.ToArray();
    }

    private static RepositoryApiMethod Query(ConfiguredQueryModel query) => new(RepositoryOperationSignature.Query(query),
        CanonicalType(ClosedRepositorySourceRenderer.ContractType(ProjectionSourceRenderer.RenderQueryReturnType(query), query.InfrastructurePrefix)),
        ProjectionSourceRenderer.CreateQueryParameters(query).Select(parameter => new RepositoryApiParameter(parameter.Name.TrimStart('@'),
            CanonicalType(ClosedRepositorySourceRenderer.ContractType(parameter.Type, query.InfrastructurePrefix)))).Append(Token()).ToArray());

    private static RepositoryApiMethod Command(ConfiguredCommandModel command) => new(RepositoryOperationSignature.Command(command), CanonicalType(command.ReturnTypeName),
        command.Parameters.Select(static parameter => new RepositoryApiParameter(parameter.Name.TrimStart('@'), CanonicalType(parameter.TypeName))).Append(Token()).ToArray());

    private static RepositoryApiParameter Token() => new("cancellationToken", "global::System.Threading.CancellationToken", true, "default");

    private static string CanonicalType(string type) => type.Replace("<int>", "<global::System.Int32>", StringComparison.Ordinal)
            .Replace("<bool>", "<global::System.Boolean>", StringComparison.Ordinal) switch
    {
        "int" => "global::System.Int32",
        "bool" => "global::System.Boolean",
        var other => other
    };

    internal static RepositoryApiMethod PortMethod(MethodInfo method, NullabilityInfoContext nullability)
        => new(method.Name + "(" + string.Join(",", method.GetParameters().Take(method.GetParameters().Length -
                                                                                (method.GetParameters().LastOrDefault()?.ParameterType == typeof(CancellationToken) ? 1 : 0))
                .Select(static parameter => RepositoryModelFactory.CSharpTypeName(parameter.ParameterType))) + ")", RepositoryPortBinding.TypeName(nullability.Create(method.ReturnParameter)),
            method.GetParameters().Select(parameter => new RepositoryApiParameter(parameter.Name!, RepositoryPortBinding.TypeName(nullability.Create(parameter)),
                parameter.IsOptional, parameter.IsOptional ? DefaultValue(parameter) : null)).ToArray(), method.IsAbstract);

    internal static string DefaultValue(ParameterInfo parameter)
    {
        if (!parameter.HasDefaultValue)
        {
            return "<missing>";
        }

        if (parameter.DefaultValue is null)
        {
            return parameter.ParameterType.IsValueType ? "default" : "null";
        }

        return Convert.ToString(parameter.DefaultValue, CultureInfo.InvariantCulture) ?? "null";
    }
}