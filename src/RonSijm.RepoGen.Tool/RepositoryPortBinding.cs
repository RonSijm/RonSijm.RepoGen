using System.Reflection;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static class RepositoryPortBinding
{
    public static EntityRepositoryModel Apply(EntityRepositoryModel entity, RepositoryEntityDesign? design)
    {
        if (design is null)
        {
            return entity;
        }

        if ((!entity.ReadEnabled && design.ReadPorts.Count > 0) || (!entity.WriteEnabled && design.WritePorts.Count > 0))
        {
            throw new InvalidOperationException($"RGD1101: Entity '{entity.EntityTypeName}' binds a port whose capability is disabled.");
        }

        var queries = entity.Queries.Select(query => (Signature: RepositoryOperationSignature.Query(query), Query: query, Operation: new Operation(query.MethodName,
            ClosedRepositorySourceRenderer.ContractType(ProjectionSourceRenderer.RenderQueryReturnType(query), query.InfrastructurePrefix),
            ProjectionSourceRenderer.CreateQueryParameters(query).Select(parameter => ClosedRepositorySourceRenderer.ContractType(parameter.Type, query.InfrastructurePrefix)).Append(RepositoryModelFactory.CSharpTypeName(typeof(CancellationToken))).ToArray()))).ToArray();
        var reads = queries.Where(static item => item.Query.Target == RepositoryQueryTarget.Read)
            .ToDictionary(static item => item.Signature, static item => item.Operation, StringComparer.Ordinal);
        var commands = entity.Commands.ToDictionary(RepositoryOperationSignature.Command, static command => new Operation(command.MethodName,
            command.ReturnTypeName,
            command.Parameters.Select(static parameter => parameter.TypeName).Append(RepositoryModelFactory.CSharpTypeName(typeof(CancellationToken))).ToArray()), StringComparer.Ordinal);
        foreach (var query in queries.Where(static item => item.Query.Target == RepositoryQueryTarget.Write))
        {
            commands.Add(query.Signature, query.Operation);
        }

        return entity with { ReadPorts = Bind(design.ReadPorts, reads), WritePorts = Bind(design.WritePorts, commands) };
    }

    private static BoundPortModel[] Bind(IEnumerable<Type> ports, IReadOnlyDictionary<string, Operation> operations)
    {
        var nullability = new NullabilityInfoContext();
        return ports.Select(port =>
        {
            var interfaces = port.GetInterfaces().Append(port).ToArray();
            if (interfaces.Any(static type => type.GetProperties().Length > 0 || type.GetEvents().Length > 0))
            {
                throw new InvalidOperationException($"RGD1102: Port '{port.FullName}' contains properties or events. Only named operation methods can be bound.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var methods = new Dictionary<string, RepositoryApiMethod>(StringComparer.Ordinal);
            foreach (var method in interfaces.SelectMany(static type => type.GetMethods()))
            {
                if (!method.IsAbstract && !method.IsStatic)
                {
                    // Keep default behavior application-owned, but include supported signatures in an optional API baseline.
                    if (!method.IsGenericMethod && method.GetParameters().All(static parameter => !parameter.ParameterType.IsByRef))
                    {
                        var defaultMethod = RepositoryApiSnapshot.PortMethod(method, nullability);
                        methods.TryAdd(defaultMethod.Signature, defaultMethod);
                    }

                    continue;
                }

                if (method.IsStatic || method.IsGenericMethod || method.GetParameters().Any(static parameter => parameter.ParameterType.IsByRef))
                {
                    throw new InvalidOperationException($"RGD1102: Port member '{port.FullName}.{method.Name}' must be a non-generic instance operation without ref/out parameters.");
                }

                var signature = RepositoryOperationSignature.Port(method);
                if (!operations.TryGetValue(signature, out var operation))
                {
                    if (operations.Values.Any(candidate => candidate.Name == method.Name))
                    {
                        throw new InvalidOperationException($"RGD1104: Port member '{port.FullName}.{method.Name}' has an incompatible input signature. Declare an overload with these exact input CLR types and a final CancellationToken.");
                    }

                    throw new InvalidOperationException($"RGD1103: Port member '{port.FullName}.{method.Name}' has no matching configured operation on this capability.");
                }

                var returnType = TypeName(nullability.Create(method.ReturnParameter));
                var parameters = method.GetParameters().Select(parameter => TypeName(nullability.Create(parameter))).ToArray();
                if (Normalize(returnType) != Normalize(operation.ReturnType) || !parameters.Select(Normalize).SequenceEqual(operation.Parameters.Select(Normalize)))
                {
                    throw new InvalidOperationException($"RGD1104: Port member '{port.FullName}.{method.Name}' has an incompatible signature or nullability. Expected {operation.ReturnType} ({string.Join(", ", operation.Parameters)}).");
                }

                names.Add(signature);
                var apiMethod = RepositoryApiSnapshot.PortMethod(method, nullability);
                if (methods.TryGetValue(signature, out var prior) && (prior.ReturnType != apiMethod.ReturnType || !prior.Parameters.SequenceEqual(apiMethod.Parameters)))
                {
                    throw new InvalidOperationException($"RGD1105: Port '{port.FullName}' inherits incompatible declarations for '{signature}' (including parameter names/defaults).");
                }

                methods[signature] = apiMethod;
            }

            return new BoundPortModel(RepositoryModelFactory.CSharpTypeName(port), names.Order(StringComparer.Ordinal).ToArray(), methods.Values.ToArray(),
                interfaces.Select(static type => RepositoryModelFactory.CSharpTypeName(type)).Order(StringComparer.Ordinal).ToArray(),
                interfaces.SelectMany(static type => type.GetMethods()).Where(static method => !method.IsGenericMethod && !method.IsStatic &&
                                                                                               method.GetParameters().All(static parameter => !parameter.ParameterType.IsByRef))
                    .SelectMany(static method => method.GetParameters().Select(static parameter => parameter.ParameterType).Append(method.ReturnType)).Distinct().ToArray());
        }).OrderBy(static port => port.TypeName, StringComparer.Ordinal).ToArray();
    }

    private static string Normalize(string type) => type.Replace("<int>", "<global::System.Int32>", StringComparison.Ordinal)
            .Replace("<bool>", "<global::System.Boolean>", StringComparison.Ordinal) switch
    {
        "int" => "global::System.Int32",
        "bool" => "global::System.Boolean",
        var other => other
    };

    internal static string TypeName(NullabilityInfo info)
        => RepositoryTypeName.Annotated(info);

    private sealed record Operation(string Name, string ReturnType, IReadOnlyList<string> Parameters);
}