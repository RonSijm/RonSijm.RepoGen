namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAdditionalReferences;

public sealed record ChangeMemberships(int CollectionId, int ExpectedVersion, IReadOnlyList<MembershipInput> Items);
