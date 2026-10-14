using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryOptionalReferences;

public interface IAssignmentWriter
{
    Task<CommandResult<WorkspaceView>> ReplaceAsync(ChangeAssignments request, CancellationToken cancellationToken = default);
    Task<CommandResult> StageAsync(ChangeAssignments request, CancellationToken cancellationToken = default);
}
