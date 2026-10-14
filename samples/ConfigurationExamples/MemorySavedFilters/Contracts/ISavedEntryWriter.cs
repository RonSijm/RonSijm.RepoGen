using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemorySavedFilters;

public interface ISavedEntryWriter
{
    Task<CommandResult<SavedEntryCard>> RenameAsync(SavedEntryInput input, CancellationToken cancellationToken = default);
    Task<CommandResult<IReadOnlyList<SavedEntryCard>>> RenameManyAsync(IReadOnlyList<SavedEntryInput> inputs, CancellationToken cancellationToken = default);
    Task<CommandResult<SavedEntryCard>> CreateAsync(SavedEntryInput input, CancellationToken cancellationToken = default);
    Task<CommandResult<IReadOnlyList<SavedEntryCard>>> CreateManyAsync(IReadOnlyList<SavedEntryInput> inputs, CancellationToken cancellationToken = default);
    Task<CommandResult<SavedEntryCard>> ArchiveAsync(int id, CancellationToken cancellationToken = default);
}
