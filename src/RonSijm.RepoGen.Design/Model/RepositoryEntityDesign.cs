using System.Collections.ObjectModel;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryEntityDesign
{
    internal RepositoryNavigationVisibilityConfiguration NavigationVisibility { get; } = new();
    public bool? NavigationVisibilityReviewRequired => NavigationVisibility.Required;
    public IReadOnlyList<RepositoryNavigationVisibilityReview> NavigationVisibilityReviews => NavigationVisibility.Reviews;
    private readonly HashSet<string> excludedProperties = new(StringComparer.Ordinal);
    private readonly HashSet<string> protectedWriteProperties = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> protectedMappedWriteProperties = new(StringComparer.Ordinal);
    private readonly List<RepositoryAccessorDesign> accessors = new();
    private readonly List<RepositoryFilterDesign> filters = new();
    private readonly RepositoryLoadingConfiguration loading = new();
    private readonly List<RepositoryLoadProfileDesign> loadProfiles = new();
    private readonly List<RepositoryProjectionDesign> projections = new();
    private readonly List<RepositorySortDesign> defaultSorts = new();
    private readonly List<RepositorySortableFieldDesign> sortableFields = new();
    private readonly List<RepositoryQueryDesign> queries = new();
    private readonly List<RepositoryCommandDesign> commands = new();
    private readonly List<RepositoryQueryFragmentDesign> fragments = new();
    private readonly List<Type> readPorts = new();
    private readonly List<Type> writePorts = new();
    private readonly List<RepositoryInMemoryNavigationDesign> inMemoryNavigations = new();
    private readonly List<RepositoryQueryFragmentDesign> visibilityPolicies = new();
    public IReadOnlyList<RepositoryQueryFragmentDesign> VisibilityPolicies => visibilityPolicies.AsReadOnly();
    internal List<RepositoryQueryFragmentDesign> VisibilityPoliciesList => visibilityPolicies;

    internal RepositoryEntityDesign(Type entityType)
    {
        EntityType = entityType;
    }

    public Type EntityType { get; }

    public bool? TestDoubleEnabled { get; internal set; }

    public RepositoryInMemoryStoreDesign? InMemoryStore { get; internal set; }

    /// <summary>Explicit test-read opt-in for portable scalar filters from the final EF model.</summary>
    public bool InMemoryModelQueryFilterEnabled { get; internal set; }

    /// <summary>Separate opt-in for this scalar model filter on explicitly declared fixture-read graphs.</summary>
    public bool InMemoryModelQueryFilterNavigationsEnabled { get; internal set; }

    /// <summary>Relationships explicitly selected for detached fixture-read loading, not production loading.</summary>
    public IReadOnlyList<RepositoryInMemoryNavigationDesign> InMemoryNavigations => inMemoryNavigations.AsReadOnly();
    internal List<RepositoryInMemoryNavigationDesign> InMemoryNavigationsList => inMemoryNavigations;

    /// <summary>Null inherits model defaults; an explicit override with null Seconds uses the context/provider setting.</summary>
    public RepositoryCommandTimeoutDesign? CommandTimeout { get; internal set; }

    public bool RepositoryEnabled { get; internal set; } = true;

    public bool ReadEnabled { get; internal set; } = true;

    public bool WriteEnabled { get; internal set; } = true;

    public bool ProjectionEnabled { get; internal set; } = true;

    public IReadOnlyList<Type> ReadPorts => new ReadOnlyCollection<Type>(readPorts);
    public IReadOnlyList<Type> WritePorts => new ReadOnlyCollection<Type>(writePorts);

    public IReadOnlyCollection<string> ExcludedProperties => excludedProperties;

    /// <summary>Scalars requiring explicit named-command permission. Does not infer audit or authorization conventions.</summary>
    public IReadOnlyList<string> ProtectedWriteProperties =>
        new ReadOnlyCollection<string>(protectedWriteProperties.OrderBy(name => name, StringComparer.Ordinal).ToArray());

    public IReadOnlyList<RepositoryAccessorDesign> Accessors => new ReadOnlyCollection<RepositoryAccessorDesign>(accessors);

    public IReadOnlyList<RepositoryProjectionDesign> Projections => new ReadOnlyCollection<RepositoryProjectionDesign>(projections);

    public IReadOnlyList<RepositoryFilterDesign> Filters => new ReadOnlyCollection<RepositoryFilterDesign>(filters);

    public IReadOnlyList<RepositorySortDesign> DefaultSorts => new ReadOnlyCollection<RepositorySortDesign>(defaultSorts);

    public IReadOnlyList<RepositorySortableFieldDesign> SortableFields =>
        new ReadOnlyCollection<RepositorySortableFieldDesign>(sortableFields);

    public IReadOnlyList<RepositoryIncludeDesign> Includes => loading.Includes;
    public IReadOnlyList<string> LoadProfileNames => loading.ProfileNames;
    public IReadOnlyList<RepositoryLoadProfileDesign> LoadProfiles => loadProfiles.AsReadOnly();

    public IReadOnlyList<RepositoryQueryDesign> Queries => new ReadOnlyCollection<RepositoryQueryDesign>(queries);

    public IReadOnlyList<RepositoryCommandDesign> Commands => new ReadOnlyCollection<RepositoryCommandDesign>(commands);

    public IReadOnlyList<RepositoryQueryFragmentDesign> Fragments =>
        new ReadOnlyCollection<RepositoryQueryFragmentDesign>(fragments);

    internal HashSet<string> ExcludedPropertiesSet => excludedProperties;
    internal HashSet<string> ProtectedWritePropertiesSet => protectedWriteProperties;
    public IReadOnlyDictionary<string, Type> ProtectedMappedWriteProperties => new ReadOnlyDictionary<string, Type>(protectedMappedWriteProperties);
    internal Dictionary<string, Type> ProtectedMappedWritePropertiesDictionary => protectedMappedWriteProperties;

    internal List<RepositoryAccessorDesign> AccessorsList => accessors;

    internal List<RepositoryProjectionDesign> ProjectionsList => projections;

    internal List<RepositoryFilterDesign> FiltersList => filters;

    internal List<RepositorySortDesign> DefaultSortsList => defaultSorts;

    internal List<RepositorySortableFieldDesign> SortableFieldsList => sortableFields;

    internal List<RepositoryIncludeDesign> IncludesList => loading.IncludesList;
    internal RepositoryLoadingConfiguration Loading => loading;
    internal List<RepositoryLoadProfileDesign> LoadProfilesList => loadProfiles;

    internal List<RepositoryQueryDesign> QueriesList => queries;

    internal List<RepositoryCommandDesign> CommandsList => commands;

    internal List<RepositoryQueryFragmentDesign> FragmentsList => fragments;
    internal List<Type> ReadPortsList => readPorts;
    internal List<Type> WritePortsList => writePorts;
}
