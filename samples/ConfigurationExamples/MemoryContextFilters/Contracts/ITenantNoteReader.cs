namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryContextFilters;

public interface ITenantNoteReader
{
    Task<IReadOnlyList<TenantNoteCard>> CardsAsync(TenantNoteRequest input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantNoteCard>> UnfilteredAsync(TenantNoteRequest input, CancellationToken cancellationToken = default);
}
