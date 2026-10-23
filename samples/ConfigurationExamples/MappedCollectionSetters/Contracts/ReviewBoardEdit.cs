namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedCollectionSetters;

public sealed record ReviewBoardEdit(int Id, string Name, int Revision, IReadOnlyList<ReviewItemEdit> Items, bool IncludeItems = true);
