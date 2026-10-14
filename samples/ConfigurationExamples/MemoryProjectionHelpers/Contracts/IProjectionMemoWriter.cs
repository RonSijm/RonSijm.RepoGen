using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryProjectionHelpers;

public interface IProjectionMemoWriter
{
    Task<CommandResult<MemoCard>> RenameAsync(RenameMemo request, CancellationToken cancellationToken = default);
}
