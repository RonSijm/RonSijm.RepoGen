using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>A reusable, design-time mapping. Calls inside designer expressions are inlined before generation.</summary>
/// <remarks>Not a runtime mapper. Mappings must be self-contained expressions; SQL translation is still provider-dependent.</remarks>
public sealed class RepositoryTypeAdapter<TModel, TContract> : IRepositoryTypeAdapter
{
    internal RepositoryTypeAdapter(Expression<Func<TModel, TContract>> toContract, Expression<Func<TContract, TModel>> toModel)
        => Design = new RepositoryTypeAdapterDesign(typeof(TModel), typeof(TContract), toContract, toModel);

    public RepositoryTypeAdapterDesign Design { get; }

    public TContract ToContract(TModel value) => throw new InvalidOperationException(
        $"RGD1701: The '{Design.ModelType.Name}' adapter is only valid inside designer expressions, not at runtime.");

    public TModel ToModel(TContract value) => throw new InvalidOperationException(
        $"RGD1701: The '{Design.ContractType.Name}' adapter is only valid inside designer expressions, not at runtime.");
}
