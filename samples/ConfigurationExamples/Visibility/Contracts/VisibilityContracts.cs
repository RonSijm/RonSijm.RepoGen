// This example's application contracts; compiled by the shared EF-free Contracts project.
namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public sealed record ReviewedDocumentSummary(int Id, string Title, string? OwnerName);

public sealed record ReviewedDocumentDetails(int Id, string Title, string? OwnerName, IReadOnlyList<string> Comments);

public interface IReviewedLibrary
{
    Task<IReadOnlyList<ReviewedDocumentSummary>> GetSummariesAsync(CancellationToken cancellationToken = default);
    Task<ReviewedDocumentDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewedDocumentSummary>> GetTrustedHistoryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> GetWithoutOwnersAsync(CancellationToken cancellationToken = default);
}
