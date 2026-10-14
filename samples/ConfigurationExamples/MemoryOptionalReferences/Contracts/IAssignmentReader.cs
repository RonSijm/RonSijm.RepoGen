namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryOptionalReferences;

public interface IAssignmentReader
{
    Task<WorkspaceView?> FindAsync(int id, CancellationToken cancellationToken = default);
}
