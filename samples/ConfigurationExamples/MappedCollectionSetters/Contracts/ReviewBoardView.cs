namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedCollectionSetters;

public sealed record ReviewBoardView(int Id, string Name, int Revision, IReadOnlyList<ReviewItemSeed> Items);
