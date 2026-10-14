namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryWriteTranslations;

public interface IPatternNoteReader
{
    Task<PatternNoteCard?> FindAsync(int id, CancellationToken cancellationToken = default);
}
