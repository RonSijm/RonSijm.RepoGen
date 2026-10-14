namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryScalarEquality;

public interface IComparisonNotesReader
{
    Task<ComparisonNoteSeed?> FindAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> MatchingAsync(string caption, CancellationToken cancellationToken = default);
}
