using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelDefaults;

public interface IDefaultSettingWriter
{
    Task<CommandResult<DefaultSettingSeed>> CreateAsync(DefaultSettingSeed request, CancellationToken cancellationToken = default);
    Task<CommandResult> StageAsync(DefaultSettingSeed request, CancellationToken cancellationToken = default);
    Task<CommandResult<IReadOnlyList<DefaultSettingSeed>>> CreateManyAsync(IReadOnlyList<DefaultSettingSeed> requests, CancellationToken cancellationToken = default);
    Task<CommandResult> ResetCreditsAsync(int id, CancellationToken cancellationToken = default);
}
