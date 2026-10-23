namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAdditionalReferences;

public sealed record MembershipInput(int ItemId, string ContributorRegion, string ContributorCode, int SourceId, string Caption, int ExpectedVersion = 1);
