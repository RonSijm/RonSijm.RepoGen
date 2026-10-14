using System.Reflection;

namespace RonSijm.RepoGen.Tool;

/// <summary>C# overload identity: input CLR types, not return types, parameter names or reference nullability.</summary>
internal static class RepositoryOperationSignature
{
    public static string Query(ConfiguredQueryModel query) => Create(query.MethodName,
        ProjectionSourceRenderer.CreateQueryParameters(query).Select(parameter =>
            ClosedRepositorySourceRenderer.ContractType(query.Parameters.FirstOrDefault(input => input.Name == parameter.Name)?.ClrTypeName ??
                                                        (query.Parameters.Any(input => input.Name == parameter.Name && input.NullableReference) ? parameter.Type.TrimEnd('?') : parameter.Type), query.InfrastructurePrefix)));

    public static string Command(ConfiguredCommandModel command) => Create(command.MethodName,
        command.Parameters.Select(static parameter => parameter.ClrTypeName ?? (parameter.NullableReference ? parameter.TypeName.TrimEnd('?') : parameter.TypeName)));

    public static string Port(MethodInfo method) => Create(method.Name,
        method.GetParameters().Select(static parameter => RepositoryModelFactory.CSharpTypeName(parameter.ParameterType)).SkipLast(1));

    private static string Create(string name, IEnumerable<string> inputs) => name + "(" + string.Join(",", inputs.Select(static type => type switch
    {
        "int" => "global::System.Int32",
        "bool" => "global::System.Boolean",
        _ => type
    })) + ")";
}