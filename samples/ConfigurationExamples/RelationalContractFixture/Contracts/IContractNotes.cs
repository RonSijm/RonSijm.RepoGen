using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.ContractNotes;

public interface IContractNotes
{
    Task<ContractNoteSummary?> FindAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContractNoteSummary>> SearchAsync(string term, CancellationToken cancellationToken = default);
    Task<PagedResult<ContractNoteSummary>> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, string>> NamesAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<ContractNoteSummary> StreamAsync(CancellationToken cancellationToken = default);
}
