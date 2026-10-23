namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNestedProjections;

public sealed record BriefingCard(int Id, BriefingHeading Heading,
    IReadOnlyList<BriefingFact> Facts, IReadOnlyList<string> Words);
