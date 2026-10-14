// This example's application contracts; compiled by the shared EF-free Contracts project.
namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public sealed record BookDetailsRequest(int Id, int FirstChapter, int MaximumNotesPerChapter, int MinimumRating, bool IncludeInternalNotes);

public sealed record ChapterDetails(int Number, string Title, IReadOnlyList<string> Notes);

public sealed record BookDetails(int Id, string Title, string? Author, string? City, IReadOnlyList<ChapterDetails> Chapters, IReadOnlyList<string> Reviews);

public interface IBookLibrary
{
    Task<BookDetails?> GetBookDetailsAsync(BookDetailsRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string?>> GetAuthorNamesAsync(IReadOnlyList<string?>? titles, CancellationToken cancellationToken = default);
}
