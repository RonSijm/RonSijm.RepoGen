using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>Explicit key and detached-copy adapters for test-only entity storage.</summary>
public sealed class RepositoryInMemoryStoreDesign
{
    private readonly List<RepositoryInMemoryGeneratedValueDesign> generatedValues = new();
    private readonly List<RepositoryInMemoryValueEqualityDesign> valueEqualities = new();
    private readonly List<RepositoryInMemoryRelationshipComparerDesign> relationshipComparers = new();
    private readonly List<RepositoryInMemoryPropertyDesign> propertyMappings = new();
    internal RepositoryInMemoryStoreDesign(Type rowType, Type keyType, LambdaExpression keySelector, LambdaExpression copy)
    {
        RowType = rowType;
        KeyType = keyType;
        KeySelector = keySelector;
        Copy = copy;
    }

    public Type RowType { get; }
    public Type KeyType { get; }
    public LambdaExpression KeySelector { get; }
    public LambdaExpression Copy { get; }
    /// <summary>Optional self-contained comparer factory for fixture key identity only.</summary>
    public LambdaExpression? KeyComparer { get; internal set; }
    /// <summary>Optional scalar read mapping. The entity initializer is a declaration, not an entity factory.</summary>
    public LambdaExpression? EntityMapping { get; internal set; }
    /// <summary>Optional explicit scalar write adapter. Receives the candidate and nullable saved public row.</summary>
    public LambdaExpression? WriteMapping { get; internal set; }
    /// <summary>Separately permits exact named scalar bindings as fixture write state; identity and graphs are excluded.</summary>
    public bool MappedPropertyStateEnabled { get; internal set; }
    /// <summary>Test-only constant model insert defaults; off unless explicitly enabled on this store.</summary>
    public bool ModelDefaultsEnabled { get; internal set; }
    /// <summary>Ordered explicit factories; replacing/removing the store removes these too.</summary>
    public IReadOnlyList<RepositoryInMemoryGeneratedValueDesign> GeneratedValues => generatedValues.AsReadOnly();
    internal List<RepositoryInMemoryGeneratedValueDesign> GeneratedValuesList => generatedValues;
    /// <summary>Explicit ordinary scalar equality for tracked fixture writes, not key/query/provider comparison.</summary>
    public IReadOnlyList<RepositoryInMemoryValueEqualityDesign> ValueEqualities => valueEqualities.AsReadOnly();
    internal List<RepositoryInMemoryValueEqualityDesign> ValueEqualitiesList => valueEqualities;
    /// <summary>Explicit principal-key component comparison for fixture-read relationships only.</summary>
    public IReadOnlyList<RepositoryInMemoryRelationshipComparerDesign> RelationshipComparers => relationshipComparers.AsReadOnly();
    internal List<RepositoryInMemoryRelationshipComparerDesign> RelationshipComparersList => relationshipComparers;
    /// <summary>Exact mapped scalars supplied by public data for reads, never inferred tracked state.</summary>
    public IReadOnlyList<RepositoryInMemoryPropertyDesign> PropertyMappings => propertyMappings.AsReadOnly();
    internal List<RepositoryInMemoryPropertyDesign> PropertyMappingsList => propertyMappings;
}
