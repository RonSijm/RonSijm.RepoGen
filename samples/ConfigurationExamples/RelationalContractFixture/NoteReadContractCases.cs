using RonSijm.RepoGen.ConfigurationExamples.Contracts.ContractNotes;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.ConfigurationExamples.RelationalContracts;

// Only application ports/DTOs and Testing are needed by these assertions. No EF/provider-specific cases belong here.
internal static class NoteReadContractCases
{
    internal static RepositoryContractSuite<IContractNotes> Create() => new(new RepositoryContractCase<IContractNotes>[]
    {
        new("unique lookup and visibility", async (repository, token) =>
        {
            Ensure(await repository.FindAsync(1, token) == new ContractNoteSummary(1, "alpha"), "Visible lookup.");
            Ensure(await repository.FindAsync(3, token) is null && await repository.FindAsync(99, token) is null, "Hidden/missing lookup.");
        }),
        new("sorted designer search", async (repository, token) =>
        {
            var rows = await repository.SearchAsync("a", token);
            Ensure(rows.Count == 2 && rows[0].Id == 1 && rows[1].Id == 2, "Predicate, projection, ordering and visibility.");
        }),
        new("counted page and guards", async (repository, token) =>
        {
            var page = await repository.PageAsync(2, 1, token);
            Ensure(page.TotalCount == 2 && page.Items.Single().Id == 2 && page.Page == 2 && page.PageSize == 1, "Counted page.");
            try { await repository.PageAsync(0, 1, token); throw new InvalidOperationException("Invalid page accepted."); }
            catch (ArgumentOutOfRangeException) { }
        }),
        new("keyed projection", async (repository, token) =>
        {
            var names = await repository.NamesAsync(token);
            Ensure(names.Count == 2 && names[1] == "alpha" && names[2] == "beta", "Dictionary mapping.");
        }),
        new("cold ordered stream", async (repository, token) =>
        {
            var stream = repository.StreamAsync(token);
            for (var enumeration = 0; enumeration < 2; enumeration++)
            {
                var ids = new List<int>();
                await foreach (var row in stream.WithCancellation(token)) ids.Add(row.Id);
                Ensure(ids.Count == 2 && ids[0] == 1 && ids[1] == 2, "Each enumeration owns an ordered visible stream.");
            }
        })
    });

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
