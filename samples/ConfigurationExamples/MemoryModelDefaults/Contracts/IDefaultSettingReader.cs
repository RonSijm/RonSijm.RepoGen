namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelDefaults;

public interface IDefaultSettingReader
{
    Task<IReadOnlyList<DefaultSettingSeed>> RowsAsync(CancellationToken cancellationToken = default);
}
