using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNestedProjections;

public interface IBriefingWriter
{
    Task<CommandResult<BriefingCard>> RenameAsync(RenameBriefing request, CancellationToken cancellationToken = default);
}
