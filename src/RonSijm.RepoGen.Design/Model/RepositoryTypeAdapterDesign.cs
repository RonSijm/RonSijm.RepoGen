using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>The immutable expressions registered for one explicitly selected mapping.</summary>
public sealed class RepositoryTypeAdapterDesign
{
    internal RepositoryTypeAdapterDesign(Type model, Type contract, LambdaExpression toContract, LambdaExpression toModel)
    {
        ModelType = model;
        ContractType = contract;
        ToContract = toContract;
        ToModel = toModel;
    }

    public Type ModelType { get; }
    public Type ContractType { get; }
    public LambdaExpression ToContract { get; }
    public LambdaExpression ToModel { get; }
}
