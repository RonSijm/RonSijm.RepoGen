using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.ScriptedNotes;

public sealed record NoteSummary(int Id, string Name);
public sealed record SelectedNotes(IReadOnlyList<int> Ids);
public sealed record CreateNoteRequest(string Name);

public interface INoteLookup
{
    Task<NoteSummary?> FindAsync(int id, CancellationToken cancellationToken = default);
    Task<NoteSummary?> FindAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NoteSummary>> ListSelectedAsync(SelectedNotes request, CancellationToken cancellationToken = default);
    async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default) => await FindAsync(id, cancellationToken) is not null;
}

public interface INoteWriter
{
    Task<CommandResult<NoteSummary>> CreateAsync(CreateNoteRequest request, CancellationToken cancellationToken = default);
}
