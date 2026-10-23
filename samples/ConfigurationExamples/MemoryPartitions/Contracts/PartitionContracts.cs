namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryPartitions;

public readonly record struct PartitionKey(string Organization, string Database);
public sealed record SettingLookup(PartitionKey Partition, string Code);
public sealed record SettingView(string Code, string? Value);

// An explicit public storage adapter: the generated fixture never references the database's PartitionSetting type.
public sealed class SettingSeed
{
    public string Code { get; set; } = "";
    public string? Value { get; set; }
}

public interface ISettingLookup
{
    Task<SettingView?> FindAsync(SettingLookup request, CancellationToken cancellationToken = default);
}
