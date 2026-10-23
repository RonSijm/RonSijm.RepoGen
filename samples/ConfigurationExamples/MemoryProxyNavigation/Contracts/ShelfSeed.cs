namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.ProxyShelves;

public sealed record ShelfSeed(int Key, string Code, string Region, string Caption, bool Hidden = false);
