namespace RonSijm.RepoGen.Design;

public sealed class RepositoryDesign
{
    internal RepositoryDesign(
        IReadOnlyList<RepositoryEntityDesign> entities,
        RepositoryGenerationOptions options, IReadOnlyList<RepositoryTypeAdapterDesign> typeAdapters,
        IReadOnlyList<RepositoryInMemoryTranslationDesign> inMemoryTranslations,
        IReadOnlyList<RepositoryInMemoryModelFilterContextDesign> inMemoryModelFilterContexts)
    {
        Entities = entities;
        Options = options;
        TypeAdapters = typeAdapters;
        InMemoryTranslations = inMemoryTranslations;
        InMemoryModelFilterContexts = inMemoryModelFilterContexts;
    }

    public IReadOnlyList<RepositoryEntityDesign> Entities { get; }

    public RepositoryGenerationOptions Options { get; }
    public IReadOnlyList<RepositoryTypeAdapterDesign> TypeAdapters { get; }
    public IReadOnlyList<RepositoryInMemoryTranslationDesign> InMemoryTranslations { get; }
    public IReadOnlyList<RepositoryInMemoryModelFilterContextDesign> InMemoryModelFilterContexts { get; }
}
