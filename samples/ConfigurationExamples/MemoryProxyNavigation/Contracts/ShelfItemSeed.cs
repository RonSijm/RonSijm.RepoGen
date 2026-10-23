namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.ProxyShelves;

public sealed record ShelfItemSeed(int Key, string? ShelfCode, string? ShelfRegion, int? CategoryKey, string Caption, int Quantity, bool Hidden = false);
