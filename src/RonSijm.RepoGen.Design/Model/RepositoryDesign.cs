namespace RonSijm.RepoGen.Design;

public sealed class RepositoryDesign
{
    internal RepositoryDesign(
        IReadOnlyList<RepositoryEntityDesign> entities,
        RepositoryGenerationOptions options, IReadOnlyList<RepositoryTypeAdapterDesign> typeAdapters,
        IReadOnlyList<RepositoryInMemoryTranslationDesign> inMemoryTranslations)
    {
        Entities = entities;
        Options = options;
        TypeAdapters = typeAdapters;
        InMemoryTranslations = inMemoryTranslations;
    }

    public IReadOnlyList<RepositoryEntityDesign> Entities { get; }

    public RepositoryGenerationOptions Options { get; }
    public IReadOnlyList<RepositoryTypeAdapterDesign> TypeAdapters { get; }
    public IReadOnlyList<RepositoryInMemoryTranslationDesign> InMemoryTranslations { get; }
}
