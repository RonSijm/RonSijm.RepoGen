namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAdditionalReferences;

public sealed record CollectionView(int Id, int Version, IReadOnlyList<MembershipView> Items);
