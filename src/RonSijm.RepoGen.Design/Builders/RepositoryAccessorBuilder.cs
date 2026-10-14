namespace RonSijm.RepoGen.Design;

public sealed class RepositoryAccessorBuilder
{
    private readonly RepositoryAccessorDesign design;

    internal RepositoryAccessorBuilder(RepositoryAccessorDesign design)
    {
        this.design = design;
    }

    public RepositoryAccessorBuilder IsUnique(bool unique = true)
    {
        design.IsUnique = unique;
        design.UniquenessConfigured = true;
        return this;
    }

    /// <summary>Changes this property's input on every inferred key/index accessor as well as its explicit accessor.</summary>
    public RepositoryAccessorBuilder UseAdapter<TModel, TInput>(RepositoryTypeAdapter<TModel, TInput> adapter)
    {
        if (adapter is null)
        {
            throw new ArgumentNullException(nameof(adapter));
        }

        if (typeof(TModel) != design.PropertyType)
        {
            throw new ArgumentException($"RGD1704: Accessor '{design.PropertyName}' has CLR type '{design.PropertyType}', not adapter model type '{typeof(TModel)}'.", nameof(adapter));
        }

        if (design.InputAdapter is not null || design.ValueConverterInputType is not null)
        {
            throw new InvalidOperationException($"RGD1704: Accessor '{design.PropertyName}' already configures an input mapping. ClearInputMapping() before replacing it.");
        }

        design.InputAdapter = adapter.Design;
        design.UseInferredUniqueness = true;
        return this;
    }

    /// <summary>Uses the EF property's configured model/provider conversion in reverse for accessor inputs.</summary>
    /// <remarks>Requires an exact converter model/provider CLR type match. Does not change the EF model.</remarks>
    public RepositoryAccessorBuilder UseValueConverter<TProvider>()
    {
        if (design.InputAdapter is not null || design.ValueConverterInputType is not null)
        {
            throw new InvalidOperationException($"RGD1704: Accessor '{design.PropertyName}' already configures an input mapping. ClearInputMapping() before replacing it.");
        }

        design.ValueConverterInputType = typeof(TProvider);
        design.UseInferredUniqueness = true;
        return this;
    }

    public RepositoryAccessorBuilder ClearInputMapping()
    {
        design.InputAdapter = null;
        design.InputMapping = null;
        design.ValueConverterInputType = null;
        design.InputNullability = null;
        return this;
    }

    /// <summary>Declares the accessor input's reference annotations, including nested generic/array nodes.</summary>
    public RepositoryAccessorBuilder InputNullability<TInput>(Action<RepositoryTypeNullabilityBuilder> configure)
    {
        if (design.InputNullability is not null)
        {
            throw new InvalidOperationException($"RGN0001: Accessor '{design.PropertyName}' configures input nullability more than once.");
        }

        design.InputNullability = RepositoryTypeNullabilityBuilder.Create(typeof(TInput), configure);
        return this;
    }

    public RepositoryAccessorBuilder HasMethodName(string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("A method name is required.", nameof(methodName));
        }

        design.MethodName = methodName.Trim();
        return this;
    }
}
