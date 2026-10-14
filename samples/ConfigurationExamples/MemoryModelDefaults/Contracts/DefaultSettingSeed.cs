namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelDefaults;

public sealed record DefaultSettingSeed(int Id, int Credits = -1, bool Enabled = true,
    DefaultSettingLevel Level = DefaultSettingLevel.Unspecified, string? Origin = null);
