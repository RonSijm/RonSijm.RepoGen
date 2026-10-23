namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNativeFilters;

public interface INativeNoteReader
{
    Task<IReadOnlyList<NativeNoteCard>> CardsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NativeNoteSeed>> AllAsync(CancellationToken cancellationToken = default);
}
