// This example's application contracts; compiled by the shared EF-free Contracts project.
namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public sealed record DirectoryItemSummary(int Id, string Name, string Category);

public sealed record DirectorySearch(int MinimumId, string?[]? Categories);

public sealed record DirectoryTextSearch(int MinimumId, string? Term);

public sealed record DirectoryCountSearch(int MinimumCount);

public sealed class DirectoryCategoryCount
{
    public string Category { get; set; } = "";
    public int Count { get; set; }
}

public interface IMembershipDirectory
{
    Task<IReadOnlyList<DirectoryItemSummary>> FindByIdsAsync(int[] ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DirectoryItemSummary>> SearchAsync(DirectorySearch request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DirectoryCategoryCount>> CategoryCountsAsync(string[] categories, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DirectoryItemSummary>> SearchTextAsync(DirectoryTextSearch request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DirectoryCategoryCount>> CategoryCountsAtLeastAsync(DirectoryCountSearch request, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, DirectoryItemSummary>> FirstByCategoryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, DirectoryItemSummary>> LastByCategoryAsync(CancellationToken cancellationToken = default);
}
