using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryRegisteredQueryDesign
{
    internal RepositoryRegisteredQueryDesign(Type provider, Type request, Type result, LambdaExpression binding)
    {
        ProviderType = provider;
        RequestType = request;
        ResultType = result;
        RequestBinding = binding;
    }

    public Type ProviderType { get; }
    public Type RequestType { get; }
    public Type ResultType { get; }
    public LambdaExpression RequestBinding { get; internal set; }
}
