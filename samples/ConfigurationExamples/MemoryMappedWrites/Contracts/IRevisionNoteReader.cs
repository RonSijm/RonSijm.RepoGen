namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryMappedWrites;

public interface IRevisionNoteReader
{
    Task<IReadOnlyList<RevisionCard>> ListAsync(CancellationToken cancellationToken = default);
}
