// This example's application contracts; compiled by the shared EF-free Contracts project.
namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public sealed record StoredNoteSummary(int Id, string Text);

// Application-owned, EF-free; importing the primary persistence selector is not necessary.
public interface INoteArchiveReader
{
    Task<IReadOnlyList<StoredNoteSummary>> ListNotesAsync(CancellationToken cancellationToken = default);
}
