using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelDefaults;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryModelDefaults;

internal sealed class StoredDefaultSetting
{
    public int Id { get; set; }
    public int Credits { get; set; } = -1;
    public bool Enabled { get; set; } = true;
    public DefaultSettingLevel Level { get; set; } = DefaultSettingLevel.Unspecified;
    public string? Origin { get; set; }
}
